using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

using KeyValueSet.Base;
using KeyValueSet.DebugViews;
using KeyValueSet.Exceptions;
using KeyValueSet.Utility;

namespace KeyValueSet
{
    [DebuggerTypeProxy(typeof(GroupingSetDebugView<,>))]
    [DebuggerDisplay("Count: {Count}")]
    [Serializable]
    public partial class GroupingSet<TKey, TElement> : 
        SetBase<IGrouping<TKey, TElement>, GroupingSet<TKey, TElement>>,
        ICollection<IGrouping<TKey, TElement>>,
        IDictionary<TKey, IEnumerable<TElement>>,
        IReadOnlyDictionary<TKey, IEnumerable<TElement>>,
        ILookup<TKey, TElement>,
        ISerializable,
        IDeserializationCallback
        where TKey : notnull
    {
#region Fields
        
        // This uses the same array-based implementation as Dictionary<TKey, TValue>.

        // Constants for serialization
        private const string CapacityName = "Capacity"; // Do not rename (binary serialization)
        private const string ElementsName = "Elements"; // Do not rename (binary serialization)
        private const string ComparerName = "Comparer"; // Do not rename (binary serialization)
        private const string VersionName = "Version"; // Do not rename (binary serialization)
        
        /// <summary>Cutoff point for stackallocs. This corresponds to the number of ints.</summary>
        private const int StackAllocThreshold = 100;
        
        /// <summary>
        /// When constructing a hashset from an existing collection, it may contain duplicates,
        /// so this is used as the max acceptable excess ratio of capacity to count. Note that
        /// this is only used on the ctor and not to automatically shrink if the hashset has, e.g,
        /// a lot of adds followed by removes. Users must explicitly shrink by calling TrimExcess.
        /// This is set to 3 because capacity is acceptable as 2x rounded up to nearest prime.
        /// </summary>
        private const int ShrinkThreshold = 3;
        private const int StartOfFreeList = -3;
        
        private int[]? _buckets;
        private Entry[]? _entries;
#if TARGET_64BIT
        private ulong _fastModMultiplier;
#endif
        private int _freeList;
        private int _freeCount;
        private int _version;
        private IEqualityComparer<TKey>? _comparer;
        private SerializationInfo? _siInfo;
        private KeyCollection? _keys;

#endregion

#region Ctors

        public GroupingSet()
        {
            
        }

        public GroupingSet(IEqualityComparer<TKey>? comparer)
        {
            if (comparer != null && comparer != EqualityComparer<TKey>.Default)
                _comparer = comparer;
        }

        public GroupingSet(int capacity) 
            : this(capacity, null) { }
        
        public GroupingSet(int capacity, IEqualityComparer<TKey>? comparer)
            : this(comparer)
        {
            switch (capacity)
            {
                case < 0:
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
                    break;
                case > 0:
                    Initialize(capacity);
                    break;
            }
        }
        
        public GroupingSet(IEnumerable<IGrouping<TKey, TElement>> groupings)
            : this(groupings, null) { }

        public GroupingSet(IEnumerable<IGrouping<TKey, TElement>> groupings, IEqualityComparer<TKey>? comparer)
            : this(comparer)
        {
            switch (groupings)
            {
                case GroupingSet<TKey, TElement> other:
                    ConstructFrom(other);
                    break;
                case ICollection<IGrouping<TKey, TElement>> collection: {
                    int count = collection.Count;
                    if (count > 0)
                        Initialize(count);
                    goto default;
                }
                default:
                    UnionWith(groupings);
                    
                    if (ShouldTrimExcess)
                        TrimExcess();
                    break;
            }
        }

        protected GroupingSet(SerializationInfo info, StreamingContext context)
        {
            // We can't do anything with the keys and values until the entire graph has been
            // deserialized and we have a reasonable estimate that GetHashCode is not going to
            // fail.  For the time being, we'll just cache this.  The graph is not valid until
            // OnDeserialization has been called.
            _siInfo = info;
        }

#endregion

#region Properties

        /// <summary>Gets the <see cref="IEqualityComparer"/> object that is used to determine equality for the values in the set.</summary>
        public IEqualityComparer<TKey> Comparer => _comparer ?? EqualityComparer<TKey>.Default;

        public bool IsEmpty => m_count == 0;
        
        /// <inheritdoc />
        public ICollection<TKey> Keys => _keys!;

        /// <inheritdoc cref="IDictionary{TKey,TValue}.Values" />
        public ICollection<IGrouping<TKey, TElement>> Values => this;

        public ref Grouping<TKey, TElement> this[TKey key]
        {
            get
            {
                int location = FindItemIndex(key, out _);
                if (location < 0)
                    throw new KeyNotFoundException();
                ref Entry entry = ref _entries![location];
                return ref entry.Grouping;
            }
        }

#endregion
        
#region Public members

        /// <summary>Ensures that this hash set can hold the specified number of elements without growing.</summary>
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);

            int currentCapacity = _entries?.Length ?? 0;
            if (currentCapacity >= capacity)
                return currentCapacity;

            if (_buckets == null)
                return Initialize(capacity);

            int newSize = HashHelpers.GetPrime(capacity);
            Resize(newSize, false);
            return newSize;
        }
        
        /// <inheritdoc cref="IDictionary{TKey,TValue}.TryGetValue" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(in TKey key, [NotNullWhen(true)] out Grouping<TKey, TElement>? value)
        {
            int location = FindItemIndex(key, out _);
            if (location < 0)
            {
                value = null;
                return false;
            }
        
            ref Entry entry = ref _entries![location];
            value = entry.Grouping;
            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Add(in TKey key, IEnumerable<TElement> elements)
        {
            CreateIfNotPresent(key, out int location);
            AddToExisting(location, elements);
            return location;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in TKey key, TElement element)
        {
            CreateIfNotPresent(key, out int location);
            ref Entry entry = ref _entries![location];
            entry.Grouping.Add(element);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sealed override bool Remove(IGrouping<TKey, TElement> items) => Remove(items.Key);

        /// <inheritdoc />
        public bool Remove(TKey key)
        {
            if (_buckets == null)
                return false;

            int location = FindItemIndex(key, out _);
            if (location < 0)
                return false;

            Entry[] entries = _entries!;
            ref Entry entry = ref entries[location];
            entry.Grouping.Dispose();
            entries[location] = default;

            _freeList = location;
            _freeCount++;
            
            return true;
        }

        /// <summary>
        /// Sets the capacity of a <see cref="GroupingSet{TKey,TElement}"/> object to the actual number of elements it contains,
        /// rounded up to a nearby, implementation-specific value.
        /// </summary>
        public void TrimExcess()
        {
            int capacity = Count;

            int newSize = HashHelpers.GetPrime(capacity);
            Entry[]? oldEntries = _entries;
            int currentCapacity = oldEntries?.Length ?? 0;
            if (newSize >= currentCapacity)
            {
                return;
            }

            int oldCount = m_count;
            _version++;
            Initialize(newSize);
            Entry[] entries = _entries!;
            int count = 0;
            for (int i = 0; i < oldCount; i++)
            {
                int hashCode = oldEntries![i].HashCode; // At this point, we know we have entries.
                if (oldEntries[i].Next >= -1)
                {
                    ref Entry entry = ref entries[count];
                    entry = oldEntries[i];
                    ref int bucket = ref GetBucketRef(hashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = count + 1;
                    count++;
                }
            }

            m_count = capacity;
            _freeCount = 0;
        }

        public IDictionary<TKey, IEnumerable<TElement>> AsDictionary() => this;

        public ILookup<TKey, TElement> AsLookup() => this;
        
        public virtual Dictionary<TKey, TElement> ToDistinct(Func<IGrouping<TKey, TElement>, TElement> distinctAggregator)
        {
            Entry[] entries = _entries!;
            Dictionary<TKey, TElement> dic = new(Count);
            
            for(int i = 0; i < entries.Length; i++)
            {
                ref Entry entry = ref entries[i];
                switch (entry.Grouping._count)
                {
                    case 0:
                        Debug.Assert(false);
                        continue;
                    case 1:
                        dic.Add(entry.Grouping._key, entry.Grouping[0]);
                        break;
                    default:
                        dic.Add(entry.Grouping._key, distinctAggregator(entry.Grouping));
                        break;
                }
            }

            return dic;
        }

        public List<IGrouping<TKey, TElement>> ToList()
        {
            var list = InternalToList();
            list.TrimExcess();
            return list;
        }

        public IGrouping<TKey, TElement>[] ToArray() => InternalToList().ToArray();

        public List<KeyValuePair<TKey, TElement>> ToFlatList()
        {
            var list = InternalToFlatList();
            list.TrimExcess();
            return list;
        }

        public KeyValuePair<TKey, TElement>[] ToFlatArray() => InternalToFlatList().ToArray();

#endregion

#region ICollection members

        bool ICollection<IGrouping<TKey, TElement>>.IsReadOnly => false;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ICollection<IGrouping<TKey, TElement>>.Add(IGrouping<TKey, TElement> items) => Add(items.Key, items);

        /// <summary>Adds an element to the current set and returns a value to indicate if the element was successfully added.</summary>
        /// <param name="items">The element to add to the set.</param>
        /// <returns><see langword="true"/> if a new group was added to the set; <see langword="false"/> if the items were added to an existing group.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Add(IGrouping<TKey, TElement> items)
        {
            bool created = CreateIfNotPresent(items.Key, out int location);
            AddToExisting(location, items);
            return created;
        }

        /// <inheritdoc cref="ICollection{T}.Clear" />
        public override void Clear()
        {
            int count = m_count;
            if (count <= 0)
                return;
            
            Debug.Assert(_buckets != null, "_buckets != null");
            Debug.Assert(_entries != null, "_entries != null");

            Array.Clear(_buckets, 0, _buckets.Length);
            m_count = 0;
            _freeList = -1;
            _freeCount = 0;
            Entry[] entries = _entries!;
            for (int i = 0; i < entries.Length; i++)
            {
                ref Entry entry = ref _entries[i];
                entry.Grouping.Dispose();
            }
            Array.Clear(_entries, 0, count);
        }

        /// <inheritdoc />
        public override bool Contains(IGrouping<TKey, TElement> elements)
        {
            int location = FindItemIndex(elements.Key, out _);
            if (location < 0)
                return false;
            
            ref Entry entry = ref _entries![location];
            var elementComparer = EqualityComparer<TElement>.Default;
            
            foreach (TElement element in elements)
            {
                if (!entry.Grouping.Contains(element, elementComparer))
                    return false;
            }

            return true;
        }

        public override void CopyTo(IGrouping<TKey, TElement>[] array, int arrayIndex, int count)
        {
            // Check array index valid index into array.
            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "Value must be greater or equal to zero.");

            // Also throw if count less than 0.
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Value must be greater or equal to zero.");

            // Will the array, starting at arrayIndex, be able to hold elements? Note: not
            // checking arrayIndex >= array.Length (consistency with list of allowing
            // count of 0; subsequent check takes care of the rest)
            if (arrayIndex > array.Length || count > array.Length - arrayIndex)
                ThrowHelper.ThrowArgumentException("Array as insufficient capacity.");

            Entry[] entries = _entries!;
            for (int i = 0; i < m_count && count != 0; i++)
            {
                ref Entry entry = ref entries[i];
                if (entry.Next < -1)
                    continue;
                array[arrayIndex++] = entry.Grouping;
                count--;
            }
        }

        /// <inheritdoc />
        public override IEnumerator<IGrouping<TKey, TElement>> GetEnumerator() => new Enumerator(this);
        
        /// <inheritdoc />
        IEnumerator<KeyValuePair<TKey, IEnumerable<TElement>>> IEnumerable<KeyValuePair<TKey, IEnumerable<TElement>>>.GetEnumerator() => new DictionaryEnumerator(new Enumerator(this));

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

#endregion

#region Serialization members

        /// <inheritdoc />
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(VersionName, _version);
            if (_comparer != null)
                info.AddValue(ComparerName, _comparer, typeof(IEqualityComparer<TKey>));
            if (_buckets == null)
            {
                info.AddValue(CapacityName, 0);
            }
            else
            {
                info.AddValue(CapacityName, _buckets.Length);
                Grouping<TKey, TElement>[] array = new Grouping<TKey, TElement>[Count];
                CopyTo(array.AsSpan(), 0);
                info.AddValue(ElementsName, array, typeof(Grouping<TKey, TElement>[]));
            }
        }

        /// <inheritdoc />
        public virtual void OnDeserialization(object? sender)
        {
            if (_siInfo == null)
            {
                // It might be necessary to call OnDeserialization from a container if the
                // container object also implements OnDeserialization. We can return immediately
                // if this function is called twice. Note we set _siInfo to null at the end of this method.
                return;
            }

            int capacity = _siInfo.GetInt32(CapacityName);
            _comparer = _siInfo.GetValue(ComparerName, typeof(IEqualityComparer<TKey>)) as IEqualityComparer<TKey>;
            _freeList = -1;
            _freeCount = 0;

            if (capacity != 0)
            {
                _buckets = new int[capacity];
                _entries = new Entry[capacity];
#if TARGET_64BIT
                _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)capacity);
#endif
                
                var array = (Grouping<TKey, TElement>[])_siInfo.GetValue(ElementsName, typeof(Grouping<TKey, TElement>[]))!;
                foreach (Grouping<TKey, TElement> grouping in array)
                {
                    CreateIfNotPresent(grouping._key, out int location);
                    AddToExisting(location, grouping);
                }
            }
            else
            {
                _buckets = null;
                _entries = null;
            }

            _version = _siInfo.GetInt32(VersionName);
        }

#endregion
        
#region Internal members
        
        internal bool ShouldTrimExcess => m_count > 0 && _entries!.Length / m_count > ShrinkThreshold;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static IEqualityComparer<GroupingSet<TKey, TElement>> CreateSetComparer() => new GroupingSetEqualityComparer<TKey, TElement>();

        /// <summary>
        /// Initializes buckets and slots arrays. Uses suggested capacity by finding next prime
        /// greater than or equal to capacity.
        /// </summary>
        private int Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            var buckets = new int[size];
            var entries = new Entry[size];

            // Assign member variables after both arrays are allocated to guard against corruption from OOM if second fails.
            _freeList = -1;
            _buckets = buckets;
            _entries = entries;
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
#endif
            
            _keys = new KeyCollection(this);

            return size;
        }
        
        /// <summary>Initializes the HashSet from another HashSet with the same element type and equality comparer.</summary>
        private void ConstructFrom(GroupingSet<TKey, TElement> source)
        {
            if (source.Count == 0)
            {
                // As well as short-circuiting on the rest of the work done,
                // this avoids errors from trying to access source._buckets
                // or source._entries when they aren't initialized.
                return;
            }

            int capacity = source._buckets!.Length;
            int threshold = HashHelpers.ExpandPrime(source.Count + 1);

            if (threshold >= capacity)
            {
                _buckets = (int[])source._buckets.Clone();
                _entries = (Entry[])source._entries!.Clone();
                _freeList = source._freeList;
                _freeCount = source._freeCount;
                m_count = source.m_count;
#if TARGET_64BIT
                _fastModMultiplier = source._fastModMultiplier;
#endif
            }
            else
            {
                Initialize(source.Count);

                Entry[]? entries = source._entries;
                for (int i = 0; i < source.m_count; i++)
                {
                    ref Entry entry = ref entries![i];
                    if (entry.Next < -1)
                        continue;

                    CreateIfNotPresent(entry.Grouping._key, out int location);
                    AddToExisting(location, entry.Grouping);
                }
            }

            Debug.Assert(Count == source.Count);
        }
        
        /// <summary>Adds the specified element to the set if it's not already contained.</summary>
        /// <param name="key">The element to add to the set.</param>
        /// <param name="location">The index into <see cref="_entries"/> of the element.</param>
        /// <returns>true if the element is added to the <see cref="GroupingSet{TKey,TElement}"/> object; false if the element is already present.</returns>
        private bool CreateIfNotPresent(in TKey key, out int location)
        {
            if (_buckets == null)
            {
                Initialize(0);
            }
            Debug.Assert(_buckets != null);

            Entry[]? entries = _entries;
            Debug.Assert(entries != null, "expected entries to be non-null");

            IEqualityComparer<TKey>? comparer = _comparer;
            int hashCode;

            uint collisionCount = 0;
            ref int bucket = ref Unsafe.NullRef<int>();

            if (comparer == null)
            {
                hashCode = key.GetHashCode();
                bucket = ref GetBucketRef(hashCode);
                int i = bucket - 1; // Value in _buckets is 1-based
                if (typeof(TKey).IsValueType)
                {
                    // ValueType: Devirtualize with EqualityComparer<TValue>.Default intrinsic
                    while (i >= 0)
                    {
                        ref Entry entry = ref entries[i];
                        if (entry.HashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.Grouping._key, key))
                        {
                            location = i;
                            return false;
                        }
                        i = entry.Next;

                        collisionCount++;
                        ThrowIfCyclicEntries(collisionCount > (uint)entries.Length);
                    }
                }
                else
                {
                    // Object type: Shared Generic, EqualityComparer<TValue>.Default won't devirtualize (https://github.com/dotnet/runtime/issues/10050),
                    // so cache in a local rather than get EqualityComparer per loop iteration.
                    EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                    while (i >= 0)
                    {
                        ref Entry entry = ref entries[i];
                        if (entry.HashCode == hashCode && defaultComparer.Equals(entry.Grouping._key, key))
                        {
                            location = i;
                            return false;
                        }
                        i = entry.Next;

                        collisionCount++;
                        ThrowIfCyclicEntries(collisionCount > (uint)entries.Length);
                    }
                }
            }
            else
            {
                hashCode = comparer.GetHashCode(key);
                bucket = ref GetBucketRef(hashCode);
                int i = bucket - 1; // Value in _buckets is 1-based
                while (i >= 0)
                {
                    ref Entry entry = ref entries[i];
                    if (entry.HashCode == hashCode && comparer.Equals(entry.Grouping._key, key))
                    {
                        location = i;
                        return false;
                    }
                    i = entry.Next;

                    collisionCount++;
                    ThrowIfCyclicEntries(collisionCount > (uint)entries.Length);
                }
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeCount--;
                Debug.Assert((StartOfFreeList - entries![_freeList].Next) >= -1, "shouldn't overflow because `next` cannot underflow");
                _freeList = StartOfFreeList - entries[_freeList].Next;
            }
            else
            {
                int count = m_count;
                if (count == entries.Length)
                {
                    Resize();
                    bucket = ref GetBucketRef(hashCode);
                }
                index = count;
                m_count = count + 1;
                entries = _entries;
            }

            {
                ref Entry entry = ref entries![index];
                entry.HashCode = hashCode;
                entry.Next = bucket - 1; // Value in _buckets is 1-based
                entry.Grouping = new Grouping<TKey, TElement>(key, hashCode);
                bucket = index + 1;
                _version++;
                location = index;
            }

            // Value types never rehash
            // if (!typeof(TKey).IsValueType && collisionCount > HashHelpers.HashCollisionThreshold && comparer is NonRandomizedStringEqualityComparer)
            // {
            //     // If we hit the collision threshold we'll need to switch to the comparer which is using randomized string hashing
            //     // i.e. EqualityComparer<string>.Default.
            //     Resize(entries.Length, forceNewHashCodes: true);
            //     location = FindItemIndex(items);
            //     Debug.Assert(location >= 0);
            // }

            return true;
        }
        
        /// <summary>Gets the index of the items in <see cref="_entries"/>, or -1 if it's not in the set.</summary>
        private int FindItemIndex(in TKey key, out int hashCode)
        {
            int[]? buckets = _buckets;
            if (buckets == null)
            {
                hashCode = Comparer.GetHashCode(key);
                return -1;
            }
            
            Entry[]? entries = _entries;
            Debug.Assert(entries != null, "Expected _entries to be initialized");

            uint collisionCount = 0;
            IEqualityComparer<TKey>? comparer = _comparer;

            if (comparer == null)
            {
                hashCode = key.GetHashCode();
                if (typeof(TKey).IsValueType)
                {
                    // ValueType: Devirtualize with EqualityComparer<TValue>.Default intrinsic
                    int i = GetBucketRef(hashCode) - 1; // Value in _buckets is 1-based
                    while (i >= 0)
                    {
                        ref Entry entry = ref entries[i];
                        if (entry.HashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.Grouping._key, key))
                        {
                            return i;
                        }
                        i = entry.Next;

                        collisionCount++;
                        ThrowIfCyclicEntries(collisionCount > (uint)entries.Length);
                    }
                }
                else
                {
                    // Object type: Shared Generic, EqualityComparer<TValue>.Default won't devirtualize (https://github.com/dotnet/runtime/issues/10050),
                    // so cache in a local rather than get EqualityComparer per loop iteration.
                    EqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
                    int i = GetBucketRef(hashCode) - 1; // Value in _buckets is 1-based
                    while (i >= 0)
                    {
                        ref Entry entry = ref entries[i];
                        if (entry.HashCode == hashCode && defaultComparer.Equals(entry.Grouping._key, key))
                        {
                            return i;
                        }
                        i = entry.Next;

                        collisionCount++;
                        ThrowIfCyclicEntries(collisionCount > (uint)entries.Length);
                    }
                }
            }
            else
            {
                hashCode = comparer.GetHashCode(key);
                int i = GetBucketRef(hashCode) - 1; // Value in _buckets is 1-based
                while (i >= 0)
                {
                    ref Entry entry = ref entries[i];
                    if (entry.HashCode == hashCode && comparer.Equals(entry.Grouping._key, key))
                    {
                        return i;
                    }
                    i = entry.Next;

                    collisionCount++;
                    ThrowIfCyclicEntries(collisionCount > (uint)entries.Length);
                }
            }
            return -1;
        }

        /// <summary>Adds the <paramref name="items"/> to the bucket at the index.</summary>
        /// <param name="location">The index of in the <see cref="_buckets"/> array.</param>
        /// <param name="items">The items to add.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AddToExisting(int location, IEnumerable<TElement> items)
        {
            Debug.Assert((uint)location < (uint)_buckets!.Length, "(uint)location < (uint)_buckets!.Length");
            ref Entry entry = ref _entries![location];
            return entry.Grouping.AddRange(items);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Resize() => Resize(HashHelpers.ExpandPrime(m_count), false);

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            // Value types never rehash
            Debug.Assert(!forceNewHashCodes || !typeof(TKey).IsValueType);
            Debug.Assert(_entries != null, "_entries should be non-null");
            Debug.Assert(newSize >= _entries.Length);

            var entries = new Entry[newSize];

            int count = m_count;
            Array.Copy(_entries, entries, count);

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _buckets = new int[newSize];
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
#endif
            
            for (int i = 0; i < count; i++)
            {
                ref Entry entry = ref entries[i];
                if (entry.Next >= -1)
                {
                    ref int bucket = ref GetBucketRef(entry.HashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = i + 1;
                }
            }

            _entries = entries;
        }
        
        /// <summary>Gets a reference to the specified hashcode's bucket, containing an index into <see cref="_entries"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetBucketRef(int hashCode)
        {
            int[] buckets = _buckets!;
#if TARGET_64BIT
            return ref buckets[HashHelpers.FastMod((uint)hashCode, (uint)buckets.Length, _fastModMultiplier)];
#else
            return ref buckets[(uint)hashCode % (uint)buckets.Length];
#endif
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CopyTo(in Span<Grouping<TKey, TElement>> span, int index)
        {
            Debug.Assert(index >= 0, "index >= 0");
            int count = m_count;
            Debug.Assert(span.Length <= count - index, "span.Length <= count - index");

            Entry[] entries = _entries!;
            for (int i = 0; i < count; i++)
            {
                ref Entry entry = ref entries[i];
                span[i] = entry.Grouping;
            }
        }

        protected virtual List<KeyValuePair<TKey, TElement>> InternalToFlatList()
        {
            List<KeyValuePair<TKey, TElement>> list = new(Count * 3 / 2);
            Entry[] entries = _entries!;
            for (int i = 0; i < entries.Length; i++)
            {
                ref Entry entry = ref entries[i];
                TKey key = entry.Grouping._key;
                list.AddRange(entry.Grouping.Select(x => KeyValuePair.Create(key, x)));
            }

            return list;
        }

        protected virtual List<IGrouping<TKey, TElement>> InternalToList()
        {
            List<IGrouping<TKey, TElement>> list = new(Count * 3 / 2);
            Entry[] entries = _entries!;
            for (int i = 0; i < entries.Length; i++)
            {
                ref Entry entry = ref entries[i];
                // Groupings may dispose outside of our & user control, we need to clone them.
                list.Add(new Grouping<TKey, TElement>(entry.Grouping));
            }
            
            return list;
        }
        
        private static void ThrowIfCyclicEntries([DoesNotReturnIf(true)] bool isCyclic)
        {
            if (isCyclic)
                // The chain of entries forms a loop, which means a concurrent update has happened.
                ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
        }
        
#endregion

        private struct Entry
        {
            public int HashCode;
            
            /// <summary>
            /// 0-based index of next entry in chain: -1 means end of chain
            /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
            /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
            /// </summary>
            public int Next;

            public Grouping<TKey, TElement> Grouping;
        }
    }
}
