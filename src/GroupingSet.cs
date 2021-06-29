using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

using KeyValueCollection.Base;
using KeyValueCollection.DebugViews;
using KeyValueCollection.Exceptions;
using KeyValueCollection.Extensions;
using KeyValueCollection.Grouping;
using KeyValueCollection.Utility;

namespace KeyValueCollection
{
    [DebuggerDisplay("Count: {Count}")]
    [DebuggerTypeProxy(typeof(GroupingSetDebugView<,>))]
    [Serializable]
    public partial class GroupingSet<TKey, TElement> : 
        HashSetBase<IGrouping<TKey, TElement>, GroupingSet<TKey, TElement>>,
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
        internal const int ShrinkThreshold = 3;
        private const int StartOfFreeList = -3;
        
        private int[]? _buckets;
        private ValueGrouping<TKey, TElement>[]? _entries;
#if TARGET_64BIT
        private ulong _fastModMultiplier;
#endif
        private int _freeList;
        private int _freeCount;
        private int _version;
        private IEqualityComparer<TKey>? _comparer;
        private SerializationInfo? _siInfo;
        private KeyCollection? _keys;
        private ValueCollection? _values;

#endregion

#region Ctors

        public GroupingSet() { }

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
            if (capacity < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            if (capacity > 0)
                Initialize(capacity);
        }
        
        public GroupingSet(IEnumerable<IGrouping<TKey, TElement>> groupings)
            : this(groupings, null) { }

        public GroupingSet(IEnumerable<IGrouping<TKey, TElement>> groupings, IEqualityComparer<TKey>? comparer)
            : this(comparer)
        {
            int count;
            switch (groupings)
            {
                case GroupingSet<TKey, TElement> other:
                    ConstructFrom(other);
                    break;
                case ICollection<IGrouping<TKey, TElement>> collection:
                    count = collection.Count;
                    if (count > 0)
                        Initialize(count);
                    goto default;
                case IReadOnlyCollection<IGrouping<TKey, TElement>> sequence:
                    count = sequence.Count;
                    if (count > 0)
                        Initialize(count);
                    goto default;
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
        public ICollection<TKey> Keys => _keys ?? (ICollection<TKey>)Array.Empty<TKey>();

        ///<inheritdoc />
        public ICollection<IEnumerable<TElement>> Values => _values  ?? (ICollection<IEnumerable<TElement>>)Array.Empty<IEnumerable<TElement>>();

        public ref ValueGrouping<TKey, TElement> this[TKey key]
        {
            get
            {
                int location = FindItemIndex(key, out _);
                if (location < 0)
                    ThrowHelper.ThrowKeyNotFoundException();
                return ref _entries![location];
            }
        }

#endregion
        
#region Public members

        /// <summary>Ensures that this hash set can hold the specified number of elements without growing.</summary>
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);

            int currentCapacity = _entries != null ? _entries.Length : 0;
            if (currentCapacity >= capacity)
                return currentCapacity;

            if (_buckets != null)
            {
                int newSize = HashHelpers.GetPrime(capacity);
                Resize(newSize, false);
                return newSize;
            }

            return Initialize(capacity);
        }
        
        /// <inheritdoc cref="IDictionary{TKey,TValue}.TryGetValue" />
        public bool TryGetValue(in TKey key, [NotNullWhen(true)] out ValueGrouping<TKey, TElement>? value)
        {
            int location = FindItemIndex(key, out _);
            if (location >= 0)
            {
                value = _entries![location];
                return true;
            }

            value = null;
            return false;
        }
        
        public int Add(in TKey key, IEnumerable<TElement> elements)
        {
            CreateIfNotPresent(key, out int location);
            _entries![location].AddRange(elements);
            return location;
        }

        public void Add(in TKey key, TElement element)
        {
            CreateIfNotPresent(key, out int location);
            _entries![location].Add(element);
        }

        /// <inheritdoc />
        public sealed override bool Remove(IGrouping<TKey, TElement> items) => Remove(items.Key);

        /// <inheritdoc />
        public bool Remove(TKey key)
        {
            if (_buckets == null)
                return false;

            int location = FindItemIndex(key, out _);
            if (location < 0)
                return false;

            ClearEntry(ref _entries![location]);
            _freeList = location;
            _freeCount++;
            
            return true;
        }

        /// <summary>
        /// Sets the capacity of a <see cref="KeyValueCollection.GroupingSet{TKey,TElement}"/> object to the actual number of elements it contains,
        /// rounded up to a nearby, implementation-specific value.
        /// </summary>
        public void TrimExcess()
        {
            int capacity = Count;

            int newSize = HashHelpers.GetPrime(capacity);
            ValueGrouping<TKey, TElement>[]? oldEntries = _entries;
            int currentCapacity = oldEntries?.Length ?? 0;
            if (newSize >= currentCapacity)
                return;

            int oldCount = m_count;
            _version++;
            Initialize(newSize);
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            int count = 0;
            for (int i = 0; i < oldCount; i++)
            {
                int hashCode = oldEntries![i].HashCode; // At this point, we know we have entries.
                if (oldEntries[i].Next >= -1)
                {
                    ref ValueGrouping<TKey, TElement> entry = ref entries![count];
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
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            Dictionary<TKey, TElement> dic = new(Count);
            
            for(int i = 0; i < entries!.Length; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                switch (entry.Count)
                {
                    case 0:
                        continue;
                    case 1:
                        dic.Add(entry.Key, entry[0]);
                        break;
                    default:
                        dic.Add(entry.Key, distinctAggregator(entry.ToImmutable()));
                        break;
                }
            }

            return dic;
        }

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
        void ICollection<IGrouping<TKey, TElement>>.Add(IGrouping<TKey, TElement> items) => Add(items.Key, items);

        /// <summary>Adds an element to the current set and returns a value to indicate if the element was successfully added.</summary>
        /// <param name="items">The element to add to the set.</param>
        /// <returns><see langword="true"/> if a new group was added to the set; <see langword="false"/> if the items were added to an existing group.</returns>
        public override bool Add(IGrouping<TKey, TElement> items)
        {
            bool created = CreateIfNotPresent(items.Key, out int location);
            _entries![location].AddRange(items);
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
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < entries.Length; i++)
                ClearEntry(ref entries[i]);
            Array.Clear(_entries, 0, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearEntry(ref ValueGrouping<TKey, TElement> entry)
        {
            entry.Elements = null;
            entry = default!;
        }

        /// <inheritdoc />
        public override bool Contains(IGrouping<TKey, TElement> elements) => GroupingContainsElements(elements.Key, elements);

        public override void CopyTo(IGrouping<TKey, TElement>[] array, int arrayIndex, int count)
        {
            if (arrayIndex < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument.arrayIndex, arrayIndex);

            if (count < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument.count, count);

            if (arrayIndex > array.Length || count > array.Length - arrayIndex)
                ThrowHelper.ThrowArgumentException_ArrayCapacity(ExceptionArgument.array);

            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < m_count && count != 0; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    array[arrayIndex++] = entry.ToImmutable();
                    count--;
                }
            }
        }

        /// <inheritdoc />
        public override IEnumerator<IGrouping<TKey, TElement>> GetEnumerator() => new Enumerator(this);
        
        /// <inheritdoc />
        IEnumerator<KeyValuePair<TKey, IEnumerable<TElement>>> IEnumerable<KeyValuePair<TKey, IEnumerable<TElement>>>.GetEnumerator() => new DictionaryEnumerator(this);

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
                ValueGrouping<TKey, TElement>[] array = new ValueGrouping<TKey, TElement>[Count];
                CopyTo(array.AsSpan(), 0);
                info.AddValue(ElementsName, array, typeof(ValueGrouping<TKey, TElement>[]));
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
                ValueGrouping<TKey, TElement>[] entries = _entries = new ValueGrouping<TKey, TElement>[capacity];
#if TARGET_64BIT
                _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)capacity);
#endif

                var array = (ValueGrouping<TKey, TElement>[])_siInfo.GetValue(ElementsName, typeof(ValueGrouping<TKey, TElement>[]))!;
                foreach (ValueGrouping<TKey, TElement> entry in array)
                {
                    CreateIfNotPresent(entry.Key, out int location);
                    if (entry.Elements != null)
                        entries[location].AddRange((IEnumerable<TElement>)entry.Elements);
                }
            }
            else
            {
                _buckets = null;
                _entries = null;
            }

            _version = _siInfo.GetInt32(VersionName);

            _siInfo = null;
        }

#endregion
        
#region Internal members
        
        internal bool ShouldTrimExcess => m_count > 0 && _entries!.Length / m_count > ShrinkThreshold;

        internal static IEqualityComparer<GroupingSet<TKey, TElement>> CreateSetComparer() => new GroupingSetEqualityComparer<TKey, TElement>();


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
        
        /// <summary>
        /// Initializes buckets and slots arrays. Uses suggested capacity by finding next prime
        /// greater than or equal to capacity.
        /// </summary>
        private int Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            var buckets = new int[size];
            var entries = new ValueGrouping<TKey, TElement>[size];

            // Assign member variables after both arrays are allocated to guard against corruption from OOM if second fails.
            _freeList = -1;
            _buckets = buckets;
            _entries = entries;
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
#endif
            
            _keys = new KeyCollection(this);
            _values = new ValueCollection(this);

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
                _entries = (ValueGrouping<TKey, TElement>[])source._entries!.Clone();
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

                ValueGrouping<TKey, TElement>[]? entries = source._entries;
                for (int i = 0; i < source.m_count; i++)
                {
                    ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                    if (entry.Next >= -1)
                    {
                        CreateIfNotPresent(entry.Key, out int location);
                        if (entry.Elements != null)
                            entries[location].AddRange((IEnumerable<TElement>)entry.Elements);
                    }
                }
            }

            Debug.Assert(Count == source.Count);
        }
        
        /// <summary>Adds the specified element to the set if it's not already contained.</summary>
        /// <param name="key">The element to add to the set.</param>
        /// <param name="location">The index into <see cref="_entries"/> of the element.</param>
        /// <returns>true if the element is added to the <see cref="KeyValueCollection.GroupingSet{TKey,TElement}"/> object; false if the element is already present.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CreateIfNotPresent(in TKey key, out int location)
        {
            if (_buckets == null)
            {
                Initialize(0);
            }
            Debug.Assert(_buckets != null);

            ValueGrouping<TKey, TElement>[]? entries = _entries;
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
                        ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                        if (entry.HashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.Key, key))
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
                        ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                        if (entry.HashCode == hashCode && defaultComparer.Equals(entry.Key, key))
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
                    ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                    if (entry.HashCode == hashCode && comparer.Equals(entry.Key, key))
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
                Debug.Assert(StartOfFreeList - entries![_freeList].Next >= -1, "shouldn't overflow because `next` cannot underflow");
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
                ref ValueGrouping<TKey, TElement> entry = ref entries![index];
                entry.HashCode = hashCode;
                entry.Next = bucket - 1; // Value in _buckets is 1-based
                entry.KeyValue = key;
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindItemIndex(in TKey key, out int hashCode)
        {
            int[]? buckets = _buckets;
            if (buckets == null)
            {
                hashCode = Comparer.GetHashCode(key);
                return -1;
            }
            
            ValueGrouping<TKey, TElement>[]? entries = _entries;
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
                        ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                        if (entry.HashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.Key, key))
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
                        ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                        if (entry.HashCode == hashCode && defaultComparer.Equals(entry.Key, key))
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
                    ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                    if (entry.HashCode == hashCode && comparer.Equals(entry.Key, key))
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

        private void Resize() => Resize(HashHelpers.ExpandPrime(m_count), false);

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            // Value types never rehash
            Debug.Assert(!forceNewHashCodes || !typeof(TKey).IsValueType);
            Debug.Assert(_entries != null, "_entries should be non-null");
            Debug.Assert(newSize >= _entries.Length);

            var entries = new ValueGrouping<TKey, TElement>[newSize];

            int count = m_count;
            Array.Copy(_entries, entries, count);

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _buckets = new int[newSize];
#if TARGET_64BIT
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
#endif
            
            for (int i = 0; i < count; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                if (entry.Next >= -1)
                {
                    ref int bucket = ref GetBucketRef(entry.HashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = i + 1;
                }
            }

            _entries = entries;
        }
        
        internal void CopyTo(in Span<ValueGrouping<TKey, TElement>> span, int index)
        {
            Debug.Assert(index >= 0, "index >= 0");
            int count = m_count;
            Debug.Assert(span.Length <= count - index, "span.Length <= count - index");

            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < count; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                span[i] = entry;
            }
        }

        protected virtual List<KeyValuePair<TKey, TElement>> InternalToFlatList()
        {
            List<KeyValuePair<TKey, TElement>> list = new(Count * 3 / 2);
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < entries!.Length; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                TKey key = entry.Key;
                foreach (TElement element in entry)
                    list.Add(new(key, element));
            }

            return list;
        }

        protected override List<IGrouping<TKey, TElement>> InternalToList()
        {
            List<IGrouping<TKey, TElement>> list = new(Count * 3 / 2);
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < entries!.Length; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                // Groupings may dispose outside of our & user control, we need to clone them.
                list.Add(entry.ToImmutable());
            }
            
            return list;
        }
        
        private static void ThrowIfCyclicEntries([DoesNotReturnIf(true)] bool isCyclic)
        {
            if (isCyclic)
                // The chain of entries forms a loop, which means a concurrent update has happened.
                ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
        }

        internal bool GroupingContainsElements(TKey key, in IEnumerable<TElement> elements)
        {
            if (FindItemIndex(key, out int location) >= 0)
            {
                ref ValueGrouping<TKey, TElement> entry = ref _entries![location];
                return entry.ContainsAll(elements, EqualityComparer<TElement>.Default);
            }

            return false;
        }
        
#endregion
    }
}
