using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

using KeyValueCollection.DebugViews;
using KeyValueCollection.Exceptions;
using KeyValueCollection.Grouping;
using KeyValueCollection.Utility;

namespace KeyValueCollection
{
    [DebuggerDisplay("Count: {Count}")]
    [DebuggerTypeProxy(typeof(GroupingSetDebugView<,>))]
    [Serializable]
    public class GroupingSet<TKey, TElement> : 
        ISet<IGrouping<TKey, TElement>>,
#if NET5_0
        IReadOnlySet<IGrouping<TKey, TElement>>,
#else
        IReadOnlyCollection<IGrouping<TKey, TElement>>,
#endif
        ICollection,
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

        private int _count;
        private int[]? _buckets;
        private Entry[]? _entries;
        private ulong _fastModMultiplier;
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

        public GroupingSet(IReadOnlyDictionary<TKey, TElement> dictionary)
            : this(dictionary, null) { }

        public GroupingSet(IReadOnlyDictionary<TKey, TElement> dictionary, IEqualityComparer<TKey>? comparer)
            : this(comparer)
        {
            Initialize(dictionary.Count);
            Entry[]? entries = _entries;
            foreach((TKey key, TElement element) in dictionary)
            {
                // Always true, since the keys in the dictionary are distinct
                CreateIfNotPresent(key, out int location);
                ref Entry entry = ref entries![location];
                entry.Grouping = new Grouping<TKey, TElement>(entry.Key, entry.HashCode);
                entry.Grouping.Add(element);
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

        /// <inheritdoc cref="ILookup{TKey,TElement}.Count" />
        public int Count => _count;

        /// <summary>Gets the <see cref="IEqualityComparer"/> object that is used to determine equality for the values in the set.</summary>
        public IEqualityComparer<TKey> Comparer => _comparer ?? EqualityComparer<TKey>.Default;

        /// <summary>Indicates whether the set is empty.</summary>
        public bool IsEmpty => _count == 0;

        /// <inheritdoc />
        public ICollection<TKey> Keys => _keys ?? (ICollection<TKey>)Array.Empty<TKey>();

        ///<inheritdoc />
        public ICollection<IEnumerable<TElement>> Values => _values  ?? (ICollection<IEnumerable<TElement>>)Array.Empty<IEnumerable<TElement>>();

        /// <summary>Returns the reference to the grouping.</summary>
        /// <value>The key of the grouping.</value>
        public Grouping<TKey, TElement> this[TKey key]
        {
            get
            {
                int location = FindItemIndex(key, out _);
                if (location < 0)
                    ThrowHelper.ThrowKeyNotFoundException();
                return _entries![location].Grouping;
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

        /// <inheritdoc cref="IDictionary{TKey,TElement}.Add(TKey,TElement)" />
        public int Add(TKey key, IEnumerable<TElement> elements)
        {
            if (CreateIfNotPresent(key, out int location))
            {
                ref Entry entry = ref _entries![location];
                entry.Grouping = new Grouping<TKey, TElement>(key, entry.HashCode);
                return entry.Grouping.AddRange(elements);
            }
            return _entries![location].Grouping.AddRange(elements);
        }

        /// <inheritdoc cref="IDictionary{TKey,TElement}.Add(TKey,TElement)" />
        public void Add(TKey key, TElement element)
        {
            if (element == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.item);
            if (CreateIfNotPresent(key, out int location))
            {
                ref Entry entry = ref _entries![location];
                entry.Grouping = new Grouping<TKey, TElement>(key, entry.HashCode);
                entry.Grouping.Add(element);
            }
            else
            {
                _entries![location].Grouping.Add(element);
            }
        }

        /// <summary>Creates a grouping with the key, and the elements to the grouping if the grouping does not exists.</summary>
        /// <param name="key">The key of the grouping.</param>
        /// <param name="elements">The elements in the collection.</param>
        /// <returns>If the grouping already exists -1; otherwise, the number of elements added to the created grouping.</returns>
        public int AddIfNotExists(TKey key, IEnumerable<TElement> elements)
        {
            if (CreateIfNotPresent(key, out int location))
            {
                ref Entry entry = ref _entries![location];
                entry.Grouping = new Grouping<TKey, TElement>(key, entry.HashCode);
                return entry.Grouping.AddRange(elements);
            }
            return -1;
        }

        /// <summary>Creates a grouping with the key, and the element to the grouping if the grouping does not exists.</summary>
        /// <param name="key">The key of the grouping.</param>
        /// <param name="element">The element to add.</param>
        /// <returns>If the grouping already exists false; otherwise, true.</returns>
        public bool AddIfNotExists(TKey key, TElement element)
        {
            if (element == null)
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.item);
            if (CreateIfNotPresent(key, out int location))
            {
                ref Entry entry = ref _entries![location];
                entry.Grouping = new Grouping<TKey, TElement>(key, entry.HashCode);
                entry.Grouping.Add(element);
                return true;
            }
            return false;
        }

        /// <inheritdoc />
        public bool Remove(IGrouping<TKey, TElement> items) => Remove(items.Key);

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
            _count--;

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
                return;

            int oldCount = _count;
            _version++;
            Initialize(newSize);
            Entry[]? entries = _entries;
            int count = 0;
            for (int i = 0; i < oldCount; i++)
            {
                int hashCode = oldEntries![i].HashCode; // At this point, we know we have entries.
                if (oldEntries[i].Next >= -1)
                {
                    ref Entry entry = ref entries![count];
                    entry = oldEntries[i];
                    ref int bucket = ref GetBucketRef(hashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = count + 1;
                    count++;
                }
            }

            _count = capacity;
            _freeCount = 0;
        }

        /// <summary>Casts the set to <see cref="ICollection{T}"/>.</summary>
        public ICollection<IGrouping<TKey, TElement>> AsEnumerable() => this;

        /// <summary>Casts the set to <see cref="IDictionary{TKey, TItem}"/>.</summary>
        public IDictionary<TKey, IEnumerable<TElement>> AsDictionary() => this;

        /// <summary>Casts the set to <see cref="ILookup{TKey, TElement}"/>.</summary>
        public ILookup<TKey, TElement> AsLookup() => this;

        /// <summary>Creates a <see cref="Dictionary{TKey, TElement}"/> from the set using a aggregator function to obtain a element representing the grouping.</summary>
        /// <param name="distinctAggregator">The aggregator function used to obtain a element representing the grouping.</param>
        /// <returns>A new <see cref="Dictionary{TKey, TElement}"/> with all keys of the set.</returns>
        public virtual Dictionary<TKey, TElement> ToDistinct(Func<IGrouping<TKey, TElement>, TElement> distinctAggregator)
        {
            Entry[]? entries = _entries;
            Dictionary<TKey, TElement> dic = new(Count);

            for(int i = 0; i < entries!.Length; i++)
            {
                ref Entry entry = ref entries[i];
                switch (entry.Grouping.Count)
                {
                    case 0:
                        continue;
                    case 1:
                        dic.Add(entry.Key, entry.Grouping.Elements![0]);
                        break;
                    default:
                        dic.Add(entry.Key, distinctAggregator(entry.Grouping));
                        break;
                }
            }

            return dic;
        }

        /// <summary>Flattens the set assigning each element is each grouping the respective key.</summary>
        /// <returns>A list of <see cref="KeyValuePair{TKey, TElement}"/>s representing each element in each grouping and the respective key.</returns>
        public List<KeyValuePair<TKey, TElement>> ToFlatList()
        {
            var list = InternalToFlatList();
            list.TrimExcess();
            return list;
        }

        /// <summary>Flattens the set assigning each element is each grouping the respective key.</summary>
        /// <returns>A array of <see cref="KeyValuePair{TKey, TElement}"/>s representing each element in each grouping and the respective key.</returns>
        public KeyValuePair<TKey, TElement>[] ToFlatArray() => InternalToFlatList().ToArray();

#endregion

#region ICollection members

        /// <inheritdoc />
        bool ICollection.IsSynchronized => false;

        /// <inheritdoc />
        object ICollection.SyncRoot => null!;

        /// <inheritdoc />
        bool ICollection<IGrouping<TKey, TElement>>.IsReadOnly => false;

        /// <inheritdoc />
        void ICollection<IGrouping<TKey, TElement>>.Add(IGrouping<TKey, TElement> items) => Add(items.Key, items);

        /// <summary>Adds an element to the current set and returns a value to indicate if the element was successfully added.</summary>
        /// <param name="items">The element to add to the set.</param>
        /// <returns><see langword="true"/> if a new group was added to the set; <see langword="false"/> if the items were added to an existing group.</returns>
        public bool Add(IGrouping<TKey, TElement> items)
        {
            if (CreateIfNotPresent(items.Key, out int location))
            {
                ref Entry entry = ref _entries![location];
                entry.Grouping = new Grouping<TKey, TElement>(items.Key, entry.HashCode);
                entry.Grouping.AddRange(items);
                return true;
            }
            _entries![location].Grouping.AddRange(items);
            return false;
        }

        /// <inheritdoc cref="ICollection{T}.Clear" />
        public void Clear()
        {
            int count = _count;
            if (count <= 0)
                return;
            
            Debug.Assert(_buckets != null, "_buckets != null");
            Debug.Assert(_entries != null, "_entries != null");

            Array.Clear(_buckets, 0, _buckets.Length);
            _count = 0;
            _freeList = -1;
            _freeCount = 0;
            Entry[]? entries = _entries;
            for (int i = 0; i < entries.Length; i++)
                ClearEntry(ref entries[i]);
            Array.Clear(entries, 0, count);
        }

        /// <inheritdoc cref="ICollection{T}.Contains" />
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(IGrouping<TKey, TElement> elements) => GroupingContainsElements(elements.Key, elements);

        public void CopyTo(IGrouping<TKey, TElement>[] array, int arrayIndex, int count)
        {
            if (arrayIndex < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument.arrayIndex, arrayIndex);

            if (count < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument.count, count);
            if (count > array.Length - arrayIndex)
                ThrowHelper.ThrowArgumentException_InsufficentArrayCapacity(ExceptionArgument.array);
            
            Enumerator en = new(this);
            for (int index = 0; index < count && en.MoveNext(); index++)
            {
                array[index] = en.CurrentValue;
            }
        }

        /// <inheritdoc />
        [Pure]
        public IEnumerator<IGrouping<TKey, TElement>> GetEnumerator() => new Enumerator(this);

        /// <inheritdoc />
        IEnumerator<KeyValuePair<TKey, IEnumerable<TElement>>> IEnumerable<KeyValuePair<TKey, IEnumerable<TElement>>>.GetEnumerator() => new DictionaryEnumerator(this);

        public static IEqualityComparer<GroupingSet<TKey, TElement>> CreateSetComparer() => new GroupingSetEqualityComparer<TKey, TElement>();

        /// <inheritdoc />
        void ICollection.CopyTo(Array array, int index)
        {
            CopyTo((IGrouping<TKey, TElement>[])array, index);
        }

#endregion

#region Serialization members

        /// <inheritdoc />
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(VersionName, _version);
            if (_comparer != null)
            {
                info.AddValue(ComparerName, _comparer, typeof(IEqualityComparer<TKey>));
            }
            
            if (_buckets == null)
            {
                info.AddValue(CapacityName, 0);
            }
            else
            {
                info.AddValue(CapacityName, _buckets.Length);
                Grouping<TKey, TElement>[] array = new Grouping<TKey, TElement>[Count];
                CopyTo(array.AsSpan());
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
                Entry[] entries = _entries = new Entry[capacity];
                if (IntPtr.Size == 8)
                {
                    _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)capacity);   
                }

                var array = (Grouping<TKey, TElement>[])_siInfo.GetValue(ElementsName, typeof(Grouping<TKey, TElement>[]))!;
                foreach (Grouping<TKey, TElement> grouping in array)
                {
                    CreateIfNotPresent(grouping.Key, out int location); // Always false
                    entries[location].Grouping = grouping;
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


#region IDictionary members

        /// <inheritdoc />
        IEnumerable<TKey> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.Keys => Keys;

        /// <inheritdoc />
        IEnumerable<IEnumerable<TElement>> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.Values => Values;

        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.IsReadOnly => false;

        /// <inheritdoc cref="IDictionary{TKey,TValue}.this" />
        IEnumerable<TElement> IDictionary<TKey, IEnumerable<TElement>>.this[TKey key]
        {
            get => this[key];
            set
            {
                if (CreateIfNotPresent(key, out int location))
                {
                    ref Entry entry = ref _entries![location];
                    entry.Grouping = new Grouping<TKey, TElement>(entry.Key, entry.HashCode);
                    entry.Grouping.AddRange(value);
                }
                else
                {
                    _entries![location].Grouping.AddRange(value);
                }
            }
        }

        /// <inheritdoc />
        IEnumerable<TElement> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.this[TKey key] => this[key];
        
        bool IDictionary<TKey, IEnumerable<TElement>>.TryGetValue(TKey key, [NotNullWhen(true)] out IEnumerable<TElement> value)
        {
            int location = FindItemIndex(key, out _);
            if (location >= 0)
            {
                value = _entries![location].Grouping;
                return true;
            }

            value = null!;
            return false;
        }

        bool IReadOnlyDictionary<TKey, IEnumerable<TElement>>.TryGetValue(TKey key, [NotNullWhen(true)] out IEnumerable<TElement> value)
        {
            int location = FindItemIndex(key, out _);
            if (location >= 0)
            {
                value = _entries![location].Grouping;
                return true;
            }

            value = null!;
            return false;
        }

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Add(KeyValuePair<TKey, IEnumerable<TElement>> item) => Add(item.Key, item.Value);

        /// <inheritdoc />
        void IDictionary<TKey, IEnumerable<TElement>>.Add(TKey key, IEnumerable<TElement> value) => Add(key, value);

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Contains(KeyValuePair<TKey, IEnumerable<TElement>> item) => GroupingContainsElements(item.Key, item.Value);

        /// <inheritdoc cref="IDictionary{TKey,TValue}.ContainsKey" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(TKey key) => FindItemIndex(key, out _) >= 0;

        /// <inheritdoc cref="IDictionary{TKey,TValue}.TryGetValue" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(TKey key, [NotNullWhen(true)] out Grouping<TKey, TElement> value)
        {
            int location = FindItemIndex(key, out _);
            if (location >= 0)
            {
                value = _entries![location].Grouping;
                return true;
            }

            value = null!;
            return false;
        }

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Remove(KeyValuePair<TKey, IEnumerable<TElement>> item)
        {
            if (_buckets == null)
                return false;

            int location = FindItemIndex(item.Key, out _);
            if (location < 0)
                return false;

            Grouping<TKey, TElement> grouping = _entries![location].Grouping;
            var elementComparer = EqualityComparer<TElement>.Default;

            foreach (TElement element in item.Value)
                grouping.Remove(element, elementComparer);

            return true;
        }

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.CopyTo(KeyValuePair<TKey, IEnumerable<TElement>>[] array, int arrayIndex)
        {
            if (arrayIndex < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument.arrayIndex, arrayIndex);
            if (array.Length - arrayIndex < Count)
                ThrowHelper.ThrowArgumentException_InsufficentArrayCapacity(ExceptionArgument.array);
            int count = _count;
            Entry[]? entries = _entries;
            Enumerator en = new(this);
            for(int index = 0; index < count && en.MoveNext(); index++)
            {
                ref Entry entry = ref entries![en.Index];
                array[arrayIndex + index] = new KeyValuePair<TKey, IEnumerable<TElement>>(entry.Key, entry.Grouping);
            }
        }

#endregion

#region ILookup member

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey key) => ContainsKey(key);

        /// <inheritdoc />
        IEnumerable<TElement> ILookup<TKey, TElement>.this[TKey key] => this[key];

#endregion

#region Set members

        /// <inheritdoc />
        public void UnionWith(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            foreach (IGrouping<TKey, TElement> element in other)
                Add(element);
        }
        
        /// <inheritdoc />
        public void ExceptWith(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // This is already the empty set; return.
            if (Count == 0)
                return;

            // Special case if other is this; a set minus itself is the empty set.
            if (ReferenceEquals(other, this))
            {
                Clear();
                return;
            }

            // Remove every element in other from this.
            foreach (IGrouping<TKey, TElement> element in other)
                Remove(element);
        }
        
        /// <inheritdoc />
        public void IntersectWith(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // Intersection of anything with empty set is empty set, so return if count is 0.
            // Same if the set intersecting with itself is the same set.
            if (Count == 0 || ReferenceEquals(other, this))
            {
                return;
            }

            // If other is known to be empty, intersection is empty set; remove all elements, and we're done.
            if (other is ICollection<Grouping<TKey, TElement>> otherAsCollection)
            {
                if (otherAsCollection.Count == 0)
                {
                    Clear();
                    return;
                }

                // Faster if other is a hashset using same equality comparer; so check
                // that other is a hashset using the same equality comparer.
                if (other is GroupingSet<TKey, TElement> otherAsSet && EqualityComparersAreEqual(otherAsSet))
                {
                    IntersectWithHashSetWithSameComparer(otherAsSet);
                    return;
                }
            }

            IntersectWithEnumerable(other);
        }

        /// <inheritdoc cref="ISet{T}.IsProperSubsetOf" />
        public bool IsProperSubsetOf(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // No set is a proper subset of itself.
            if (ReferenceEquals(other, this))
            {
                return false;
            }

            if (other is ICollection<Grouping<TKey, TElement>> otherAsCollection)
            {
                // No set is a proper subset of an empty set.
                if (otherAsCollection.Count == 0)
                {
                    return false;
                }

                // The empty set is a proper subset of anything but the empty set.
                if (Count == 0)
                {
                    return otherAsCollection.Count > 0;
                }

                // Faster if other is a hashset (and we're using same equality comparer).
                if (other is GroupingSet<TKey, TElement> otherAsSet && EqualityComparersAreEqual(otherAsSet))
                {
                    if (Count >= otherAsSet.Count)
                        return false;

                    // This has strictly less than number of items in other, so the following
                    // check suffices for proper subset.
                    return IsSubsetOfHashSetWithSameComparer(otherAsSet);
                }
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount > 0;
        }
        
        /// <inheritdoc cref="ISet{T}.IsProperSupersetOf" />
        public bool IsProperSupersetOf(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // The empty set isn't a proper superset of any set, and a set is never a strict superset of itself.
            if (Count == 0 || ReferenceEquals(other, this))
                return false;

            if (other is ICollection<Grouping<TKey, TElement>> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    // Note that this has at least one element, based on above check.
                    return true;
                }

                // Faster if other is a hashset with the same equality comparer
                if (other is GroupingSet<TKey, TElement> otherAsSet && EqualityComparersAreEqual(otherAsSet))
                {
                    if (otherAsSet.Count >= Count)
                        return false;

                    // Now perform element check.
                    return ContainsAllElements(otherAsSet);
                }
            }

            // Couldn't fall out in the above cases; do it the long way
            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
            return uniqueCount < Count && unfoundCount == 0;
        }

        /// <inheritdoc cref="ISet{T}.IsSubsetOf" />
        public bool IsSubsetOf(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // The empty set is a subset of any set, and a set is a subset of itself.
            // Set is always a subset of itself
            if (Count == 0 || ReferenceEquals(other, this))
            {
                return true;
            }

            // Faster if other has unique elements according to this equality comparer; so check
            // that other is a hashset using the same equality comparer.
            if (other is GroupingSet<TKey, TElement> otherAsSet && EqualityComparersAreEqual(otherAsSet))
            {
                // if this has more elements then it can't be a subset
                if (Count > otherAsSet.Count)
                {
                    return false;
                }

                // already checked that we're using same equality comparer. simply check that
                // each element in this is contained in other.
                return IsSubsetOfHashSetWithSameComparer(otherAsSet);
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount >= 0;
        }

        /// <inheritdoc cref="ISet{T}.IsSupersetOf" />
        public bool IsSupersetOf(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // A set is always a superset of itself.
            if (ReferenceEquals(other, this))
            {
                return true;
            }

            // Try to fall out early based on counts.
            if (other is ICollection<Grouping<TKey, TElement>> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    return true;
                }

                // Try to compare based on counts alone if other is a hashset with same equality comparer.
                if (other is GroupingSet<TKey, TElement> otherAsSet &&
                    EqualityComparersAreEqual(otherAsSet) &&
                    otherAsSet.Count > Count)
                {
                    return false;
                }
            }

            return ContainsAllElements(other);
        }

        /// <inheritdoc cref="ISet{T}.Overlaps" />
        public bool Overlaps(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            if (Count == 0)
            {
                return false;
            }

            // Set overlaps itself
            if (ReferenceEquals(other, this))
            {
                return true;
            }

            foreach (IGrouping<TKey, TElement> element in other)
            {
                if (Contains(element))
                    return true;
            }

            return false;
        }

        /// <inheritdoc cref="ISet{T}.SetEquals" />
        public bool SetEquals(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // A set is equal to itself.
            if (ReferenceEquals(other, this))
                return true;

            // Faster if other is a hashset and we're using same equality comparer.
            if (other is GroupingSet<TKey, TElement> otherAsSet && EqualityComparersAreEqual(otherAsSet))
            {
                // Attempt to return early: since both contain unique elements, if they have
                // different counts, then they can't be equal.
                if (Count != otherAsSet.Count)
                {
                    return false;
                }

                // Already confirmed that the sets have the same number of distinct elements, so if
                // one is a superset of the other then they must be equal.
                return ContainsAllElements(otherAsSet);
            }
            else
            {
                // If this count is 0 but other contains at least one element, they can't be equal.
                if (Count == 0 &&
                    other is ICollection<Grouping<TKey, TElement>> otherAsCollection &&
                    otherAsCollection.Count > 0)
                {
                    return false;
                }

                (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
                return uniqueCount == Count && unfoundCount == 0;
            }
        }

        /// <inheritdoc />
        public void SymmetricExceptWith(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            // If set is empty, then symmetric difference is other.
            if (Count == 0)
            {
                UnionWith(other);
                return;
            }

            // Special-case this; the symmetric difference of a set with itself is the empty set.
            if (ReferenceEquals(other, this))
            {
                Clear();
                return;
            }

            // If other is a HashSet, it has unique elements according to its equality comparer,
            // but if they're using different equality comparers, then assumption of uniqueness
            // will fail. So first check if other is a hashset using the same equality comparer;
            // symmetric except is a lot faster and avoids bit array allocations if we can assume
            // uniqueness.
            if (other is GroupingSet<TKey, TElement> otherAsSet && EqualityComparersAreEqual(otherAsSet))
            {
                SymmetricExceptWithUniqueHashSet(otherAsSet);
            }
            else
            {
                SymmetricExceptWithEnumerable(other);
            }
        }
        
        /// <inheritdoc cref="ICollection{T}.CopyTo"/>
        public void CopyTo(IGrouping<TKey, TElement>[] array) => CopyTo(array, 0, Count);

        /// <inheritdoc cref="ICollection{T}.CopyTo"/>
        public void CopyTo(IGrouping<TKey, TElement>[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);
        
        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public List<IGrouping<TKey, TElement>> ToList()
        {
            var list = InternalToList();
            list.TrimExcess();
            return list;
        }
        
        /// <summary>
        /// Checks if equality comparers are equal. This is used for algorithms that can
        /// speed up if it knows the other item has unique elements. I.e. if they're using
        /// different equality comparers, then uniqueness assumption between sets break.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool EqualityComparersAreEqual(GroupingSet<TKey, TElement> other) => Comparer.Equals(other.Comparer);

        /// <summary>
        /// Checks if this contains of other's elements. Iterates over other's elements and
        /// returns false as soon as it finds an element in other that's not in this.
        /// Used by SupersetOf, ProperSupersetOf, and SetEquals.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool ContainsAllElements(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            foreach (IGrouping<TKey, TElement> element in other)
            {
                if (ContainsKey(element.Key))
                    continue;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines counts that can be used to determine equality, subset, and superset. This
        /// is only used when other is an IEnumerable and not a HashSet. If other is a HashSet
        /// these properties can be checked faster without use of marking because we can assume
        /// other has no duplicates.
        ///
        /// The following count checks are performed by callers:
        /// 1. Equals: checks if unfoundCount = 0 and uniqueFoundCount = m_count; i.e. everything
        /// in other is in this and everything in this is in other
        /// 2. Subset: checks if unfoundCount >= 0 and uniqueFoundCount = m_count; i.e. other may
        /// have elements not in this and everything in this is in other
        /// 3. Proper subset: checks if unfoundCount > 0 and uniqueFoundCount = m_count; i.e
        /// other must have at least one element not in this and everything in this is in other
        /// 4. Proper superset: checks if unfound count = 0 and uniqueFoundCount strictly less
        /// than m_count; i.e. everything in other was in this and this had at least one element
        /// not contained in other.
        ///
        /// An earlier implementation used delegates to perform these checks rather than returning
        /// an ElementCount struct; however this was changed due to the perf overhead of delegates.
        /// </summary>
        /// <param name="other"></param>
        /// <param name="returnIfUnfound">Allows us to finish faster for equals and proper superset
        /// because unfoundCount must be 0.</param>
        protected unsafe (int, int) CheckUniqueAndUnfoundElements(IEnumerable<IGrouping<TKey, TElement>> other, bool returnIfUnfound)
        {
            // Need special case in case this has no elements.
            if (_count == 0)
            {
                int numElementsInOther = 0;
                if (other.Any())
                {
                    numElementsInOther++;
                }

                return (0, numElementsInOther);
            }

            Debug.Assert(_buckets != null && _count > 0, "_buckets was null but count greater than 0");

            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThreshold];
            BitHelper bitHelper = intArrayLength <= StackAllocThreshold ? new BitHelper(span.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            int unfoundCount = 0; // count of items in other not found in this
            int uniqueFoundCount = 0; // count of unique items in other found in this

            foreach (IGrouping<TKey, TElement> grouping in other)
            {
                int location = FindItemIndex(grouping.Key, out _);
                if (location >= 0)
                {
                    if (!bitHelper.IsMarked(location))
                    {
                        // Item hasn't been seen yet.
                        bitHelper.MarkBit(location);
                        uniqueFoundCount++;
                    }
                }
                else
                {
                    unfoundCount++;
                    if (returnIfUnfound)
                        break;
                }
            }

            return (uniqueFoundCount, unfoundCount);
        }

        /// <summary>
        /// if other is a set, we can assume it doesn't have duplicate elements, so use this
        /// technique: if can't remove, then it wasn't present in this set, so add.
        ///
        /// As with other methods, callers take care of ensuring that other is a hashset using the
        /// same equality comparer.
        /// </summary>
        /// <param name="other"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SymmetricExceptWithUniqueHashSet(GroupingSet<TKey, TElement> other)
        {
            foreach (IGrouping<TKey, TElement> grouping in other)
            {
                TKey key = grouping.Key;
                if (Remove(key))
                    Add(key, grouping);
            }
        }

        /// <summary>
        /// Implementation notes:
        ///
        /// Used for symmetric except when other isn't a HashSet. This is more tedious because
        /// other may contain duplicates. HashSet technique could fail in these situations:
        /// 1. Other has a duplicate that's not in this: HashSet technique would add then
        /// remove it.
        /// 2. Other has a duplicate that's in this: HashSet technique would remove then add it
        /// back.
        /// In general, its presence would be toggled each time it appears in other.
        ///
        /// This technique uses bit marking to indicate whether to add/remove the item. If already
        /// present in collection, it will get marked for deletion. If added from other, it will
        /// get marked as something not to remove.
        ///
        /// </summary>
        /// <param name="other"></param>
        protected unsafe void SymmetricExceptWithEnumerable(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> itemsToRemoveSpan = stackalloc int[StackAllocThreshold / 2];
            BitHelper itemsToRemove = intArrayLength <= StackAllocThreshold / 2 ? new BitHelper(itemsToRemoveSpan.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            Span<int> itemsAddedFromOtherSpan = stackalloc int[StackAllocThreshold / 2];
            BitHelper itemsAddedFromOther = intArrayLength <= StackAllocThreshold / 2 ? new BitHelper(itemsAddedFromOtherSpan.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            foreach (IGrouping<TKey, TElement> grouping in other)
            {
                if (CreateIfNotPresent(grouping.Key, out int location))
                {
                    _entries![location].Grouping.AddRange(grouping);
                    // wasn't already present in collection; flag it as something not to remove
                    // *NOTE* if location is out of range, we should ignore. BitHelper will
                    // detect that it's out of bounds and not try to mark it. But it's
                    // expected that location could be out of bounds because adding the item
                    // will increase _lastIndex as soon as all the free spots are filled.
                    itemsAddedFromOther.MarkBit(location);
                }
                else
                {
                    // already there...if not added from other, mark for remove.
                    // *NOTE* Even though BitHelper will check that location is in range, we want
                    // to check here. There's no point in checking items beyond originalCount
                    // because they could not have been in the original collection
                    if (location < originalCount && !itemsAddedFromOther.IsMarked(location))
                        itemsToRemove.MarkBit(location);
                }
            }

            Entry[]? entries = _entries;
            // if anything marked, remove it
            for (int i = 0; i < originalCount; i++)
            {
                if (itemsToRemove.IsMarked(i))
                {
                    Remove(entries![i].Key);
                }
            }
        }

        /// <summary>
        /// If other is a hashset that uses same equality comparer, intersect is much faster
        /// because we can use other's Contains
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void IntersectWithHashSetWithSameComparer(GroupingSet<TKey, TElement> other)
        {
            Entry[]? entries = _entries;
            for (int i = 0; i < _count; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    if (!other.ContainsKey(entry.Key))
                        Remove(entry.Key);
                }
            }
        }

        /// <summary>
        /// Iterate over other. If contained in this, mark an element in bit array corresponding to
        /// its position in _slots. If anything is unmarked (in bit array), remove it.
        ///
        /// This attempts to allocate on the stack, if below StackAllocThreshold.
        /// </summary>
        protected unsafe void IntersectWithEnumerable(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            Debug.Assert(_buckets != null, "_buckets shouldn't be null; callers should check first");

            // Keep track of current last index; don't want to move past the end of our bit array
            // (could happen if another thread is modifying the collection).
            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThreshold];
            BitHelper bitHelper = intArrayLength <= StackAllocThreshold ? new BitHelper(span.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            // Mark if contains: find index of in slots array and mark corresponding element in bit array.
            foreach (IGrouping<TKey, TElement> item in other)
            {
                int location = FindItemIndex(item.Key, out _);
                if (location >= 0)
                    bitHelper.MarkBit(location);
            }

            Entry[]? entries = _entries;
            // If anything unmarked, remove it. Perf can be optimized here if BitHelper had a
            // FindFirstUnmarked method.
            for (int i = 0; i < originalCount; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1 && !bitHelper.IsMarked(i))
                    Remove(entry.Key);
            }
        }

        /// <summary>
        /// Implementation Notes:
        /// If other is a hashset and is using same equality comparer, then checking subset is
        /// faster. Simply check that each element in this is in other.
        ///
        /// Note: if other doesn't use same equality comparer, then Contains check is invalid,
        /// which is why callers must take are of this.
        ///
        /// If callers are concerned about whether this is a proper subset, they take care of that.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsSubsetOfHashSetWithSameComparer(GroupingSet<TKey, TElement> other)
        {
            foreach (IGrouping<TKey, TElement> item in this)
            {
                if (other.ContainsKey(item.Key))
                    continue;
                return false;
            }

            return true;
        }

#endregion
        
#region Internal members
        
        internal bool ShouldTrimExcess => _count > 0 && _entries!.Length / _count > ShrinkThreshold;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearEntry(ref Entry entry)
        {
            entry.Grouping = null!;
            entry = default!;
        }

        /// <summary>Gets a reference to the specified hashcode's bucket, containing an index into <see cref="_entries"/>.</summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetBucketRef(int hashCode)
        {
            int[] buckets = _buckets!;
            if (IntPtr.Size == 8)
            {
                return ref buckets[HashHelpers.FastMod((uint)hashCode, (uint)buckets.Length, _fastModMultiplier)];   
            }

            return ref buckets[(uint)hashCode % (uint)buckets.Length];
        }
        
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

            if (IntPtr.Size == 8)
            {
                _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
            }
            
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
                _entries = (Entry[])source._entries!.Clone();
                _freeList = source._freeList;
                _freeCount = source._freeCount;
                _count = source._count;
                
                if (IntPtr.Size == 8)
                {
                    _fastModMultiplier = source._fastModMultiplier;
                }
            }
            else
            {
                Initialize(source.Count);

                Entry[]? sourceEntries = source._entries;
                for (int i = 0; i < source._count; i++)
                {
                    ref Entry entry = ref sourceEntries![i];
                    if (entry.Next >= -1)
                    {
                        // Always true, since the keys in the grouping are distinct
                        CreateIfNotPresent(entry.Key, out int location);
                        _entries![location].Grouping = new Grouping<TKey, TElement>(entry.Grouping);
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
        private unsafe bool CreateIfNotPresent(in TKey key, out int location)
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
#if NET5_0 || NETCOREAPP3_1_OR_GREATER
            ref int bucket = ref Unsafe.AsRef<int>(null);
#else
            int __bucket = -1; // dummy element
            ref int bucket = ref __bucket;
#endif
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
                        ref Entry entry = ref entries[i];
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
                    ref Entry entry = ref entries[i];
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
                int count = _count;
                if (count == entries.Length)
                {
                    Resize();
                    bucket = ref GetBucketRef(hashCode);
                }
                index = count;
                _count = count + 1;
                entries = _entries;
            }

            {
                ref Entry entry = ref entries![index];
                entry.HashCode = hashCode;
                entry.Next = bucket - 1; // Value in _buckets is 1-based
                entry.Key = key;
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
                        ref Entry entry = ref entries[i];
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
                    ref Entry entry = ref entries[i];
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

        private void Resize() => Resize(HashHelpers.ExpandPrime(_count), false);

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            // Value types never rehash
            Debug.Assert(!forceNewHashCodes || !typeof(TKey).IsValueType);
            Debug.Assert(_entries != null, "_entries should be non-null");
            Debug.Assert(newSize >= _entries.Length);

            var entries = new Entry[newSize];

            int count = _count;
            Array.Copy(_entries, entries, count);

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _buckets = new int[newSize];

            if (IntPtr.Size == 8)
            {
                _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
            }

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
        
        internal void CopyTo(in Span<Grouping<TKey, TElement>> span)
        {
            Debug.Assert(span.Length <= _count, "span.Length <= m_count");

            Enumerator en = new(this);
            for (int index = 0; index < span.Length && en.MoveNext(); index++)
            {
                span[index] = en.CurrentValue;
            }
        }

        protected virtual List<KeyValuePair<TKey, TElement>> InternalToFlatList()
        {
            List<KeyValuePair<TKey, TElement>> list = new(Count * 3 / 2);
            Entry[]? entries = _entries;
            for (int i = 0; i < entries!.Length; i++)
            {
                ref Entry entry = ref entries![i];
                TKey key = entry.Key;
                foreach (TElement element in entry.Grouping)
                    list.Add(new(key, element));
            }

            return list;
        }

        protected List<IGrouping<TKey, TElement>> InternalToList()
        {
            List<IGrouping<TKey, TElement>> list = new(Count * 3 / 2);
            Entry[]? entries = _entries;
            for (int i = 0; i < entries!.Length; i++)
            {
                ref Entry entry = ref entries![i];
                // Groupings may dispose outside of our & user control, we need to clone them.
                list.Add(entry.Grouping);
            }
            
            return list;
        }
        
        private static void ThrowIfCyclicEntries([DoesNotReturnIf(true)] bool isCyclic)
        {
            if (isCyclic)
                // The chain of entries forms a loop, which means a concurrent update has happened.
                ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool GroupingContainsElements(TKey key, in IEnumerable<TElement> elements)
        {
            if (FindItemIndex(key, out int location) >= 0)
            {
                return _entries![location].Grouping.ContainsAll(elements, EqualityComparer<TElement>.Default);
            }

            return false;
        }

        #endregion

        private struct Entry
        {
            internal TKey Key;
            internal int HashCode;
            /// <summary>
            /// 0-based index of next entry in chain: -1 means end of chain
            /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
            /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
            /// </summary>
            internal int Next;

            internal Grouping<TKey, TElement> Grouping;
        }
        
        [DebuggerDisplay("Count: {Count}")]
        [DebuggerTypeProxy(typeof(GroupingSetKeyCollectionDebugView<,>))]
        internal sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly GroupingSet<TKey, TElement> _set;

            internal KeyCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
            }

            /// <inheritdoc cref="ICollection{T}.Count" />
            public int Count => _set.Count;

            /// <inheritdoc />
            public bool IsReadOnly => false;

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => null!;

            /// <inheritdoc />
            public void Add(TKey item) => _set.CreateIfNotPresent(item, out _);

            /// <inheritdoc />
            public void Clear() => _set.Clear();

            /// <inheritdoc />
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Contains(TKey item) => _set.ContainsKey(item);

            /// <inheritdoc />
            public void CopyTo(TKey[] array, int arrayIndex)
            {
                if (arrayIndex < 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument.arrayIndex, arrayIndex);
                if (array.Length - arrayIndex < Count)
                    ThrowHelper.ThrowArgumentException_InsufficentArrayCapacity(ExceptionArgument.array);
                Enumerator en = new(_set);
                while(en.MoveNext())
                {
                    array[arrayIndex++] = en.CurrentValue.Key;
                }
            }

            void ICollection.CopyTo(Array array, int index)
            {
                CopyTo((TKey[])array, index);
            }

            /// <inheritdoc />
            public bool Remove(TKey item) => _set.Remove(item);

            /// <inheritdoc />
            public IEnumerator<TKey> GetEnumerator() => new KeyEnumerator(_set);

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [DebuggerDisplay("Count: {Count}")]
        [DebuggerTypeProxy(typeof(GroupingSetValueCollectionDebugView<,>))]
        internal sealed class ValueCollection : ICollection<IEnumerable<TElement>>, ICollection, IReadOnlyCollection<IEnumerable<TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;

            internal ValueCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
            }

            /// <inheritdoc cref="ICollection{T}.Count" />
            public int Count => _set.Count;

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.IsReadOnly => true;

            /// <inheritdoc />
            bool ICollection.IsSynchronized => false;

            /// <inheritdoc />
            object ICollection.SyncRoot => null!;

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Add(IEnumerable<TElement> item) => ThrowHelper.ThrowNotSupportedException();

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Clear() => ThrowHelper.ThrowNotSupportedException();

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Contains(IEnumerable<TElement> item)
            {
                ThrowHelper.ThrowNotSupportedException();
                return false;
            }

            /// <inheritdoc />
            public void CopyTo(IEnumerable<TElement>[] array, int arrayIndex)
            {
                if (arrayIndex < 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException_ValueGreaterOrEqualZero(ExceptionArgument.arrayIndex, arrayIndex);
                if (array.Length - arrayIndex < Count)
                    ThrowHelper.ThrowArgumentException_InsufficentArrayCapacity(ExceptionArgument.array);
                Enumerator en = new(_set);
                while (en.MoveNext())
                {
                    array[arrayIndex++] = en.CurrentValue.ElementsSpan.ToArray();
                }
            }

            /// <inheritdoc />
            void ICollection.CopyTo(Array array, int index)
            {
                CopyTo((IEnumerable<TElement>[])array, index);
            }

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Remove(IEnumerable<TElement> item)
            {
                ThrowHelper.ThrowNotSupportedException();
                return false;
            }

            /// <inheritdoc />
            public IEnumerator<IEnumerable<TElement>> GetEnumerator() => new ValueEnumerator(_set);

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        internal struct Enumerator : IEnumerator<IGrouping<TKey, TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            internal int Index;
            internal Grouping<TKey, TElement> CurrentValue;

            internal Enumerator(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                Index = 0;
                CurrentValue = default!;
            }

            public bool MoveNext()
            {
                if (_version != _set._version)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)Index < (uint)_set._count)
                {
                    ref Entry entry = ref _set._entries![Index++];
                    if (entry.Next >= -1)
                    {
                        CurrentValue = entry.Grouping;
                        return true;
                    }
                }

                Index = _set._count + 1;
                CurrentValue = default!;
                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                Index = 0;
                CurrentValue = default!;
            }

            /// <inheritdoc />
            public IGrouping<TKey, TElement> Current => CurrentValue;

            /// <inheritdoc />
            object IEnumerator.Current => CurrentValue;

            /// <inheritdoc />
            public void Dispose() { }
        }

        internal struct DictionaryEnumerator : IEnumerator<KeyValuePair<TKey, IEnumerable<TElement>>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, IEnumerable<TElement>> _current;

            internal DictionaryEnumerator(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                _index = 0;
                _current = default!;
            }

            public bool MoveNext()
            {
                if (_version != _set._version)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)_index < (uint)_set._count)
                {
                    ref Entry entry = ref _set._entries![_index++];
                    if (entry.Next >= -1)
                    {
                        _current = new KeyValuePair<TKey, IEnumerable<TElement>>(entry.Key, entry.Grouping);
                        return true;
                    }
                }

                _index = _set._count + 1;
                _current = default!;
                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                _index = 0;
                _current = default!;
            }

            /// <inheritdoc />
            public KeyValuePair<TKey, IEnumerable<TElement>> Current => _current;

            /// <inheritdoc />
            object IEnumerator.Current => _current;

            /// <inheritdoc />
            public void Dispose() { }
        }

        internal struct KeyEnumerator : IEnumerator<TKey>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            internal int Index;
            private TKey _current;

            internal KeyEnumerator(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                Index = 0;
                _current = default!;
            }

            public bool MoveNext()
            {
                if (_version != _set._version)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)Index < (uint)_set._count)
                {
                    ref Entry entry = ref _set._entries![Index++];
                    if (entry.Next >= -1)
                    {
                        _current = entry.Key;
                        return true;
                    }
                }

                Index = _set._count + 1;
                _current = default!;
                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                Index = 0;
                _current = default!;
            }

            /// <inheritdoc />
            public TKey Current => _current;

            /// <inheritdoc />
            object IEnumerator.Current => _current;

            /// <inheritdoc />
            public void Dispose() { }
        }

        internal struct ValueEnumerator : IEnumerator<IEnumerable<TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            internal IEnumerable<TElement> CurrentValue;
            internal int Index;

            internal ValueEnumerator(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                CurrentValue = default!;
                Index = 0;
            }

            public bool MoveNext()
            {
                if (_version != _set._version)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)Index < (uint)_set._count)
                {
                    ref Entry entry = ref _set._entries![Index++];
                    if (entry.Next >= -1)
                    {
                        CurrentValue = entry.Grouping;
                        return true;
                    }
                }

                Index = _set._count + 1;
                CurrentValue = default!;
                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                Index = 0;
                CurrentValue = default!;
            }

            /// <inheritdoc />
            public IEnumerable<TElement> Current => CurrentValue;

            /// <inheritdoc />
            object IEnumerator.Current => CurrentValue;

            /// <inheritdoc />
            public void Dispose() { }
        }
    }
}
