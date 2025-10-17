using System;
using System.Collections.Generic;
using Godot;

public class RingBuffer<T>
{
    private readonly T[] _values;
    private readonly int[] _ticks;
    private int _start;
    private int _count;

    public int Capacity { get; }
    public int Count => _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be greater than zero.");

        Capacity = capacity;
        _values = new T[capacity];
        _ticks = new int[capacity];
        _start = 0;
        _count = 0;
    }

    public void Push(int tick, T value)
    {
        for (int i = 0; i < _count; i++)
        {
            int index = (_start + i) % Capacity;
            if (_ticks[index] == tick)
            {
                _values[index] = value;
                return;
            }
        }

        int newIndex = (_start + _count) % Capacity;

        if (_count == Capacity)
        {
            _start = (_start + 1) % Capacity;
            _ticks[newIndex] = tick;
            _values[newIndex] = value;
        }
        else
        {
            _ticks[newIndex] = tick;
            _values[newIndex] = value;
            _count++;
        }
    }

    public bool TryGetByTick(int tick, out T value)
    {
        for (int i = 0; i < _count; i++)
        {
            int index = (_start + i) % Capacity;
            if (_ticks[index] == tick)
            {
                value = _values[index];
                return true;
            }
        }

        value = default!;
        return false;
    }

    public int GetLatestTick()
    {
        if (_count == 0)
            throw new InvalidOperationException("Buffer is empty.");

        int index = (_start + _count - 1) % Capacity;
        return _ticks[index];
    }

    public T GetLatestValue()
    {
        if (_count == 0)
            throw new InvalidOperationException("Buffer is empty.");

        int index = (_start + _count - 1) % Capacity;
        return _values[index];
    }

    public (int Tick, T Value) GetOldest()
    {
        if (_count == 0)
            throw new InvalidOperationException("Buffer is empty.");

        return (_ticks[_start], _values[_start]);
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    public void PrintAll()
    {
        GD.Print("RingBuffer contents:");
        for (int i = 0; i < _count; i++)
        {
            int index = (_start + i) % Capacity;
            GD.Print($"Tick: {_ticks[index]}, Value: {_values[index]}");
        }
    }

    public IEnumerable<(int Tick, T Value)> Items()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = (_start + i) % Capacity;
            yield return (_ticks[index], _values[index]);
        }
    }
}
