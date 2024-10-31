using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RevitServices.Elements
{
    public class LazyHashSet<T> : IReadOnlySet<T>
    {
        private class InnerEnumerator : IEnumerator<T>
        {
            HashSet<T> _set;
            IEnumerator<T>? _setEnumerator;
            IEnumerator<T> _emptyingEnumerator;
            LazyHashSet<T> _source;

            T _current;
            int _state = 0;

            public T Current => _current;

            object IEnumerator.Current => Current;

            public InnerEnumerator(LazyHashSet<T> source)
            {
                _source = source;
                _set = _source._set;
                _emptyingEnumerator = source._sourceEnumerator;
                _state = 0;
            }

            public void Dispose()
            {
                _setEnumerator?.Dispose();
                _source._inUse = false;
            }

            public void SkipSetIteration()
            {
                if (_state <= 2)
                    _state = 2;
            }

            public bool MoveNext()
            {
                switch (_state)
                {
                    case 0:
                        if (_set.Count > 0)
                        {
                            _setEnumerator = _set.GetEnumerator();
                            _state = 1;
                            goto case 1;
                        }
                        goto case 2; // skip the set iteration, the set has no items
                    case 1:
                        _source._inUse = true;
                        var setDidAdvance = _setEnumerator!.MoveNext(); // the _setEnumerator must exist
                        if (setDidAdvance)
                        {
                            _current = _setEnumerator.Current;
                            return true;
                        }

                        _state++;
                        goto case 2; // fall through, we are onto the next enumerator
                    case 2:
                        _source._inUse = true;
                        while (_emptyingEnumerator.MoveNext())
                        {
                            var enumCurrent = _emptyingEnumerator.Current;
                            if (_set.Add(enumCurrent))
                            {
                                _current = enumCurrent;
                                return true;
                            }
                        }
                        _emptyingEnumerator.Dispose();
                        _state++; // there are no more items in this enumerator
                        break;
                }
                _source._exhausted = true;
                return false;
            }

            public void Reset() => throw new NotImplementedException();
        }

        HashSet<T> _set; // the backing hashset which is filled lazily while the enumerator is being iterated
        IEnumerator<T> _sourceEnumerator; // the source of the lazy hashset items
        bool _exhausted = false; // is the _sourceEnumerator empty?
        bool _inUse = false; // the _sourceEnumerator can only be used by one iteration at a time
        IEqualityComparer<T>? _setEqualityComparer;

        HashSet<T> HashSet // the filled hashset, will exhaust the iterator if necessary
        {
            get
            {
                if (!_exhausted)
                    Exhaust();
                return _set;
            }
        }

        /// <summary>
        /// Prefer this to .Any() or .Count for checking if there are any items in this set.
        /// </summary>
        public bool HasItems { get; }

        /// <summary>
        /// Is true until the source enumerator has been exhausted, at which point it will be false
        /// </summary>
        public bool IsLazy => !_exhausted;

        /// <summary>
        /// Gets the number of elements in the collection. 
        /// Warning: If just checking for any items, prefer <see cref="HasItems"/> to see if the collection is non-empty.
        /// </summary>
        /// <inheritdoc/>
        public int Count => HashSet.Count;

        public LazyHashSet(IEnumerable<T> enumerable)
        {
            ArgumentNullException.ThrowIfNull(enumerable);
            _sourceEnumerator = enumerable.GetEnumerator();
            HasItems = enumerable.Any();
            // EqualityComparer<TSource>.Default.Equals(element, value)
        }

        public LazyHashSet(LazyHashSet<T> source, IEqualityComparer<T> comparer) : this(source)
        {
            _setEqualityComparer = comparer;
        }

        public bool Contains(T item)
        {
            var setInitialized = _set != null;
            if (setInitialized && _set!.Contains(item)) // first try finding the item in the set
                return true;

            if (_exhausted) // there is nothing to enumerate after the set has been checked
                return false;

            // get an innerEnumerator that fills the _inner HashSet while we search for the item
            // in the remaining IEnumerable
            var iter = GetInnerEnumerator();
            try
            {
                if (setInitialized)
                    iter.SkipSetIteration(); // we already checked the set
                var comparer = _setEqualityComparer ?? EqualityComparer<T>.Default;
                while (iter.MoveNext())
                {
                    if (comparer.Equals(iter.Current, item))
                        return true;
                }
            }
            finally
            {
                iter.Dispose();
            }
            return false; // after this point, every call to .Contains will exit at _exhausted instead
        }

        private InnerEnumerator GetInnerEnumerator()
        {
            Debug.Assert(IsLazy);
            if (_inUse)
                throw new InvalidOperationException($"Cannot enumerate the {nameof(LazyHashSet<T>)} multiple times until it has been exhausted");

            _set ??= new HashSet<T>(_setEqualityComparer);
            return new InnerEnumerator(this);
        }

        /// <summary>
        /// Immediately exhausts the source enumerator, making this <see cref="LazyHashSet"/> non-lazy
        /// </summary>
        public void Exhaust()
        {
            if (_exhausted)
                return;

            var inner = GetInnerEnumerator();
            try
            {
                while (inner.MoveNext())
                    continue;
            }
            finally
            {
                inner.Dispose();
            }
            _exhausted = true;
        }

        public IEnumerator<T> GetEnumerator() => _exhausted ? HashSet.GetEnumerator() : GetInnerEnumerator();

        public bool IsProperSubsetOf(IEnumerable<T> other) => HashSet.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => HashSet.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<T> other) => HashSet.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<T> other) => HashSet.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<T> other) => HashSet.Overlaps(other);

        public bool SetEquals(IEnumerable<T> other) => HashSet.SetEquals(other);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
