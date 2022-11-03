// Copyright (c) 2021 ezequias2d <ezequiasmoises@gmail.com> and the Ez contributors
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

//namespace Ez.Memory
namespace Kyber;

/// <summary>
/// A static class with useful methods for memory manipulation operations.
/// </summary>
public static class MemUtils
{
	static readonly int PlatformWordSize = IntPtr.Size;
	static readonly int PlatformWordSizeBits = PlatformWordSize * 8;

	/// <summary>
	/// Gets the size in bytes of an unmanaged type.
	/// </summary>
	/// <typeparam name="T">The unmanaged type to measure.</typeparam>
	/// <returns>Size in bytes of <typeparamref name="T"/>.</returns>
	public static uint SizeOf<T>() where T : unmanaged
	{
		unsafe
		{
			return (uint)sizeof(T);
		}
	}

	/// <summary>
	/// Gets the size of a <see cref="ReadOnlySpan{T}"/> in bytes.
	/// </summary>
	/// <typeparam name="T">The unmanaged type to measure.</typeparam>
	/// <param name="span">The span to measure</param>
	/// <returns>Size in bytes of <paramref name="span"/>.</returns>
	public static long SizeOf<T>(ReadOnlySpan<T> span) where T : unmanaged
	{
		unsafe
		{
			return (long)span.Length * sizeof(T);
		}
	}

	/// <summary>
	/// Adds an offset to the value of a pointer.
	/// </summary>
	/// <param name="ptr">The pointer to add the offset to.</param>
	/// <param name="offset">The offset to add.</param>
	/// <returns>A new pointer that reflects the addition of <paramref name="offset"/> 
	/// to <paramref name="ptr"/>.</returns>
	public static IntPtr Add(IntPtr ptr, long offset) =>
		new IntPtr(ptr.ToInt64() + offset);

	/// <summary>
	/// Returns a value indicating whether an instance is anywhere in the array.
	/// </summary>
	/// <typeparam name="T">The unmanaged type of element to check.</typeparam>
	/// <param name="value">The value to compare.</param>
	/// <param name="list">The list of values to compare with <paramref name="value"/>.</param>
	/// <returns><see langword="true"/> if the <paramref name="value"/> parameter 
	/// is contained in the <paramref name="list"/>; otherwise, <see langword="false"/></returns>
	public static bool AnyEquals<T>(T value, params T[] list) where T : unmanaged
	{
		unsafe
		{
			fixed (T* pList = list)
			{
				T* current = pList;
				T* max = pList + list.Length;

				while (current < max)
				{
					if (Equals(current, &value, SizeOf<T>()))
						return true;
					current++;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Returns a value indicating whether a <see cref="ReadOnlySpan{T}"/> is equal 
	/// to another <see cref="ReadOnlySpan{T}"/>.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="a">The first <see cref="ReadOnlySpan{T}"/> to compare.</param>
	/// <param name="b">The second <see cref="ReadOnlySpan{T}"/> to compare.</param>
	/// <returns><see langword="true"/> if the span <paramref name="a"/> parameter 
	/// equals to span <paramref name="b"/> parameter; otherwise, <see langword="false"/></returns>
	public static bool Equals<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b) where T : unmanaged
	{
		unsafe
		{
			int count = a.Length;

			if (count != b.Length)
				return false;

			fixed (void* ptrA = a, ptrB = b)
			{
				return Equals(ptrA, ptrB, SizeOf(a));
			}
		}
	}

	/// <summary>
	/// Returns a value indicating whether the content of one pointer is equal
	/// to that of another pointer by a specified number of bytes.
	/// </summary>
	/// <param name="a">The first pointer to compare.</param>
	/// <param name="b">The second pointer to compare.</param>
	/// <param name="byteCount">The number of bytes to compare.</param>
	/// <returns><see langword="true"/> if the contents of the pointer <paramref name="a"/> 
	/// are equal to contents of the pointer <paramref name="b"/> by <paramref name="byteCount"/> bytes.</returns>
	public static unsafe bool Equals(void* a, void* b, long byteCount)
	{
		var i1 = 0L;
		var i2 = 0L;
		var i4 = 0L;
		var i8 = 0L;

		var c8 = (byteCount >> 3);
		var c4 = (byteCount - (c8 << 3)) >> 2;
		var c2 = (byteCount - (c4 << 2) - (c8 << 3)) >> 1;
		var c1 = (byteCount - (c2 << 1) - (c4 << 2) - (c8 << 3));

		byte* aPos = (byte*)a;
		byte* bPos = (byte*)b;

		while (i8 < c8)
		{
			if (*(ulong*)aPos != *(ulong*)bPos)
				return false;
			aPos += 8;
			bPos += 8;
			i8++;
		}

		while (i4 < c4)
		{
			if (*(uint*)aPos != *(uint*)bPos)
				return false;
			aPos += 4;
			bPos += 4;
			i4++;
		}

		while (i2 < c2)
		{
			if (*(ushort*)aPos != *(ushort*)bPos)
				return false;
			aPos += 2;
			bPos += 2;
			i2++;
		}

		while (i1 < c1)
		{
			if (*aPos != *bPos)
				return false;
			aPos++;
			bPos++;
			i1++;
		}

		return true;
	}

	/// <summary>
	/// Sets all bytes of a <see cref="Span{T}"/> to a specified value.
	/// </summary>
	/// <typeparam name="T">The type of items in the <paramref name="span"/>.</typeparam>
	/// <param name="span">The span to be set.</param>
	/// <param name="value">The byte value to set.</param>
	public static void Set<T>(Span<T> span, byte value) where T : unmanaged
	{
		unsafe
		{
			fixed (T* spanPtr = span)
			{
				Set(spanPtr, value, SizeOf<T>(span));
			}
		}
	}

	/// <summary>
	/// Turns a pointer into a ref <typeparamref name="T"/>.
	/// </summary>
	/// <typeparam name="T">The type of the reference.</typeparam>
	/// <param name="ptr">The pointer to referenced memory.</param>
	/// <returns>A T reference of <paramref name="ptr"/>.</returns>
	public static unsafe ref T GetRef<T>(IntPtr ptr) where T : unmanaged => ref *(T*)ptr.ToPointer();

	/// <summary>
	/// Sets all first <paramref name="byteCount"/> bytes to the <paramref name="value"/> byte. 
	/// </summary>
	/// <param name="memoryPtr">The pointer to the first byte.</param>
	/// <param name="value">The byte value to set.</param>
	/// <param name="byteCount">The number of bytes to set.</param>
	public static unsafe void Set(void* memoryPtr, byte value, long byteCount)
	{
		if (byteCount < 0)
			throw new ArgumentOutOfRangeException(nameof(byteCount));

		if (byteCount <= uint.MaxValue)
			Unsafe.InitBlockUnaligned(memoryPtr, value, (uint)byteCount);
		else
		{
			uint bc;
			byte* ptr = (byte*)memoryPtr;
			while (byteCount > 0)
			{
				bc = (uint)Math.Min(uint.MaxValue, byteCount);
				Unsafe.InitBlockUnaligned(ptr, value, bc);
				byteCount -= bc;
				ptr += bc;
			}
		}
	}

	/// <summary>
	/// Sets all first <paramref name="byteCount"/> bytes to the <paramref name="value"/> byte. 
	/// </summary>
	/// <param name="memoryPtr">The pointer to the first byte.</param>
	/// <param name="value">The byte value to set.</param>
	/// <param name="byteCount">The number of bytes to set.</param>
	public static void Set(IntPtr memoryPtr, byte value, long byteCount)
	{
		unsafe
		{
			Set((void*)memoryPtr, value, byteCount);
		}
	}

	/// <summary>
	/// Sets all the first <paramref name="count"/> Ts to the <paramref name="value"/>.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="ptr">The pointer to the first T to set.</param>
	/// <param name="value">The value to set.</param>
	/// <param name="count">The number of Ts to set.</param>
	public static unsafe void Set<T>(IntPtr ptr, in T value, long count) where T : unmanaged
	{
		var pptr = (T*)ptr;
		while (count > 0)
		{
			*pptr = value;
			count--;
			pptr++;
		}
	}

	/// <summary>
	/// Copies all data from one <see cref="ReadOnlySpan{T}"/> to a <see cref="Span{T}"/>.
	/// </summary>
	/// <typeparam name="T">The type of items in the <paramref name="destination"/> and <paramref name="source"/>.</typeparam>
	/// <param name="destination">The <see cref="Span{T}"/> that receives the data.</param>
	/// <param name="source">The <see cref="ReadOnlySpan{T}"/> that contains the data to copy.</param>
	/// <returns>The number of bytes copied.</returns>
	public static long Copy<T>(Span<T> destination, ReadOnlySpan<T> source) where T : unmanaged => Copy<T, T>(destination, source);

	/// <summary>
	/// Copies all data from <paramref name="source"/> to <paramref name="destination"/>.
	/// </summary>
	/// <typeparam name="T">The type of items in <paramref name="destination"/>.</typeparam>
	/// <param name="destination">The destination span.</param>
	/// <param name="source">The source address to copy from.</param>
	/// <returns>The number of bytes copied.</returns>
	public static long Copy<T>(Span<T> destination, IntPtr source) where T : unmanaged =>
		Copy(destination, GetSpan<T>(source, destination.Length));

	/// <summary>
	/// Copies all data from one <see cref="ReadOnlySpan{T}"/> to a <see cref="Span{T}"/>.
	/// </summary>
	/// <typeparam name="TDestination">The type of items in the <paramref name="destination"/>.</typeparam>
	/// <typeparam name="TSource">The type of items in the <paramref name="source"/>.</typeparam>
	/// <param name="destination">The <see cref="Span{T}"/> that receives the data.</param>
	/// <param name="source">The <see cref="ReadOnlySpan{T}"/> that contains the data to copy.</param>
	/// <returns>The number of bytes copied.</returns>
	public static long Copy<TDestination, TSource>(Span<TDestination> destination, ReadOnlySpan<TSource> source)
		where TDestination : unmanaged
		where TSource : unmanaged
	{
		unsafe
		{
			var srcSize = SizeOf(source);
			if (srcSize > SizeOf<TDestination>(destination))
				throw new ArgumentOutOfRangeException($"The destination is too small to copy all data from the source. \nSource size: {srcSize} bytes.\nDestination size: {SizeOf<TDestination>(destination)} bytes.");

			fixed (void* dst = destination, src = source)
				Copy(dst, src, srcSize);

			return srcSize;
		}
	}

	/// <summary>
	/// Copies all data from a <see cref="ReadOnlySpan{T}"/> to a destination address.
	/// </summary>
	/// <typeparam name="T">The type of items in the <paramref name="src"/>.</typeparam>
	/// <param name="dst">The destination address to copy to.</param>
	/// <param name="src">The <see cref="ReadOnlySpan{T}"/> that contains the data to copy.</param>
	/// <returns>The number of bytes copied.</returns>
	public static unsafe long Copy<T>(IntPtr dst, ReadOnlySpan<T> src) where T : unmanaged => Copy(dst.ToPointer(), src);

	/// <summary>
	/// Copies all data from a <see cref="ReadOnlySpan{T}"/> to a destination address.
	/// </summary>
	/// <typeparam name="T">The type of items in the <paramref name="src"/>.</typeparam>
	/// <param name="dst">The destination address to copy to.</param>
	/// <param name="src">The <see cref="ReadOnlySpan{T}"/> that contains the data to copy.</param>
	/// <returns>The number of bytes copied.</returns>
	public static unsafe long Copy<T>(void* dst, ReadOnlySpan<T> src) where T : unmanaged
	{
		fixed (void* srcPtr = src)
		{
			var size = SizeOf(src);
			Copy(dst, srcPtr, size);
			return size;
		}
	}

	/// <summary>
	/// Copies all data from a T value to a destination address.
	/// </summary>
	/// <typeparam name="T">The type of data to copy.</typeparam>
	/// <param name="dst">The destination address to copy to.</param>
	/// <param name="src">The value to copy.</param>
	/// <returns>The number of bytes copied.</returns>
	public static long Copy<T>(IntPtr dst, in T src) where T : unmanaged
	{
		unsafe
		{
			*(T*)dst.ToPointer() = src;
			return SizeOf<T>();
		}
	}

	/// <summary>
	/// Copies bytes from the source address to the destination address.
	/// </summary>
	/// <param name="destination">The destination address to copy to.</param>
	/// <param name="source">The source address to copy from.</param>
	/// <param name="byteCount">The number of bytes to copy.</param>
	public static unsafe void Copy(IntPtr destination, IntPtr source, long byteCount) => Copy((void*)destination, (void*)source, byteCount);

	/// <summary>
	/// Copies bytes from the source address to the destination address.
	/// </summary>
	/// <param name="destination">The destination address to copy to.</param>
	/// <param name="source">The source address to copy from.</param>
	/// <param name="byteCount">The number of bytes to copy.</param>
	public static unsafe void Copy(void* destination, void* source, long byteCount)
	{
		if (byteCount <= uint.MaxValue)
			Unsafe.CopyBlockUnaligned(destination, source, (uint)byteCount);
		else
		{
			uint bc;
			byte* dst = (byte*)destination;
			byte* src = (byte*)source;
			while (byteCount > 0)
			{
				bc = (uint)Math.Min(uint.MaxValue, byteCount);
				Unsafe.CopyBlockUnaligned(dst, src, bc);
				byteCount -= bc;
				dst += bc;
				src += bc;
			}
		}
	}

	/// <summary>
	/// Gets the theoretical limit for an allocation.
	/// <seealso cref="Alloc(long)"/>.
	/// </summary>
	public static readonly long MaxAllocSize = (long)IntPtr.MaxValue;

	/// <summary>
	/// Allocates memory from unmanaged memory of process.
	/// </summary>
	/// <param name="size">The required number of bytes in memory.</param>
	/// <returns>A pointer to the newly allocated memory. This memory must be released using
	/// the <see cref="Free(IntPtr)"/> method.</returns>
	public static unsafe IntPtr Alloc(long size) => Marshal.AllocHGlobal((IntPtr)size);

	/// <summary>
	/// Frees memory previously allocated from the unmanaged memory of the process.
	/// </summary>
	/// <param name="ptr">The handle returned by the original matching call to <see cref="Alloc(long)"/>.</param>
	public static unsafe void Free(IntPtr ptr) => Marshal.FreeHGlobal(ptr);

	/// <summary>
	/// Gets a <see cref="Span{T}"/> from a pointer and length.
	/// </summary>
	/// <typeparam name="T">The type of items in <see cref="Span{T}"/>.</typeparam>
	/// <param name="ptr">A pointer to the starting address.</param>
	/// <param name="length">The number of <typeparamref name="T"/> elements in <see cref="Span{T}"/></param>
	/// <returns>The span of <paramref name="ptr"/> and <paramref name="length"/> params.</returns>
	public unsafe static Span<T> GetSpan<T>(IntPtr ptr, int length) where T : unmanaged => new Span<T>((T*)ptr, length);

	///// <summary>
	///// Gets a <see cref="Memory{T}"/> from a pointer and length.
	///// </summary>
	///// <typeparam name="T">The type of items in <see cref="Memory{T}"/>.</typeparam>
	///// <param name="ptr">A pointer to the starting address.</param>
	///// <param name="length">The number of <typeparamref name="T"/> elements in <see cref="Memory{T}"/></param>
	///// <returns>The span of <paramref name="ptr"/> and <paramref name="length"/> params.</returns>
	//public unsafe static Memory<T> GetMemory<T>(IntPtr ptr, int length) where T : unmanaged => new UnmanagedMemoryManager<T>(ptr, length).Memory;

	/// <summary>
	/// Gets a null-terminated UTF-8 string from a pointer.
	/// </summary>
	/// <param name="ptr">The pointer of memory to read from.</param>
	/// <returns>A string with the characters read from <paramref name="ptr"/>.</returns>
	public static string? GetUtf8String(IntPtr ptr) => Marshal.PtrToStringUTF8(ptr);

	/// <summary>
	/// Casts a span of one primitive type to a span of another primitive type.
	/// </summary>
	/// <typeparam name="TFrom">The type of source <paramref name="span"/>.</typeparam>
	/// <typeparam name="TTo">The type of target span.</typeparam>
	/// <param name="span">The source slice to convert.</param>
	/// <returns>The converted span.</returns>
	public unsafe static Span<TTo> Cast<TFrom, TTo>(Span<TFrom> span) where TTo : unmanaged where TFrom : unmanaged => MemoryMarshal.Cast<TFrom, TTo>(span);

	/// <summary>
	/// Casts a read-only span of one primitive type to a span of another primitive type.
	/// </summary>
	/// <typeparam name="TFrom">The type of source <paramref name="span"/>.</typeparam>
	/// <typeparam name="TTo">The type of target span.</typeparam>
	/// <param name="span">The source slice to convert.</param>
	/// <returns>The converted read-only span.</returns>
	public unsafe static ReadOnlySpan<TTo> Cast<TFrom, TTo>(ReadOnlySpan<TFrom> span) where TTo : unmanaged where TFrom : unmanaged => MemoryMarshal.Cast<TFrom, TTo>(span);
}