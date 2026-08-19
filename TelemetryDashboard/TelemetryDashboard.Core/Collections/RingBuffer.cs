using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Collections;

/// <summary>
/// Thread-safe generic ring buffer with fixed capacity and zero data loss buffer behavior.
/// When capacity is reached, Enqueue drops the oldest item to accommodate new data.
/// </summary>
/// <typeparam name="T">Type of items stored in the buffer.</typeparam>
public class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly object _lock = new();
    private int _head;
    private int _tail;
    private int _count;

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public bool IsFull
    {
        get
        {
            lock (_lock)
            {
                return _count == Capacity;
            }
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _count == 0;
            }
        }
    }

    public RingBuffer(int capacity = 5000)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }
        Capacity = capacity;
        _buffer = new T[capacity];
    }

    public void Enqueue(T item)
    {
        lock (_lock)
        {
            _buffer[_tail] = item;
            _tail = (_tail + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
            else
            {
                // Buffer is full: overwrite oldest item, advance head
                _head = (_head + 1) % Capacity;
            }
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _buffer[_head];
            _buffer[_head] = default!;
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }
    }

    public List<T> Flush()
    {
        lock (_lock)
        {
            var result = new List<T>(_count);
            while (_count > 0)
            {
                result.Add(_buffer[_head]);
                _buffer[_head] = default!;
                _head = (_head + 1) % Capacity;
                _count--;
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}
