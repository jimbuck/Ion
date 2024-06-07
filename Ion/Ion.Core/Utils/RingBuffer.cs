
namespace Ion;

public class RingBuffer<T>
{
	private T[] _buffer;
	private int _capacity;
	private int _size;
	private int _head;
	private int _tail;

	public int Count => _size;

	public T this[int index] {
		get
		{
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _size);

			return _buffer[(_head + index) % _capacity];
		}
	}

	public RingBuffer(int capacity)
	{
		if (capacity <= 0) throw new ArgumentException("Capacity must be greater than 0.");

		_capacity = capacity;
		_buffer = new T[capacity];
		_size = 0;
		_head = 0;
		_tail = 0;
	}

	public void Add(T item)
	{
		if (_size == _capacity)
		{
			// Double the capacity and copy elements to the new buffer.
			int newCapacity = _capacity * 2;
			T[] newBuffer = new T[newCapacity];
			for (int i = 0; i < _size; i++)
			{
				newBuffer[i] = _buffer[(_head + i) % _capacity];
			}
			_buffer = newBuffer;
			_head = 0;
			_tail = _size;
			_capacity = newCapacity;
		}

		_buffer[_tail] = item;
		_tail = (_tail + 1) % _capacity;
		_size++;
	}

	public void Clear()
	{
		_size = 0;
		_head = 0;
		_tail = 0;
	}
}