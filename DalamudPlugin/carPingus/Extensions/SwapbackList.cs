using System.Collections;
using System.Collections.Generic;

namespace carPingus.Extensions;

public class SwapbackList<T> : IList<T>
{
    public T this[int index]
    {
        get => list[index];
        set => list[index] = value;
    }

    public int Count => list.Count;

    public bool IsReadOnly => ((ICollection<T>)list).IsReadOnly;

    private readonly List<T> list = [];

    public void Add(T item)
    {
        list.Add(item);
    }

    public void Clear()
    {
        list.Clear();
    }

    public bool Contains(T item)
    {
        return list.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        list.CopyTo(array, arrayIndex);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return list.GetEnumerator();
    }

    public int IndexOf(T item)
    {
        return list.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        list.Insert(index, item);
    }

    public bool Remove(T item)
    {
        var index = list.IndexOf(item);
        if (index >= 0)
        {
            list[index] = list[^1];
            list.RemoveAt(Count - 1);
            return true;
        }
        return false;
    }

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < Count)
        {
            list[index] = list[^1];
            list.RemoveAt(list.Count - 1);
        }
        else
        {
            throw new System.ArgumentOutOfRangeException(nameof(index));
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }
}
