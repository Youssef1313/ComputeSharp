﻿using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using ComputeSharp.Core.Helpers;
using ComputeSharp.Exceptions;
using ComputeSharp.Graphics.Commands;
using ComputeSharp.Graphics.Extensions;
using ComputeSharp.Graphics.Helpers;
using ComputeSharp.Graphics.Resources.Enums;
using ComputeSharp.Graphics.Resources.Interop;
using ComputeSharp.Resources;
using ComputeSharp.Resources.Views;
using Microsoft.Toolkit.Diagnostics;
using static TerraFX.Interop.D3D12_COMMAND_LIST_TYPE;
using FX = TerraFX.Interop.Windows;

namespace ComputeSharp
{
    /// <summary>
    /// A <see langword="class"/> representing a typed read write buffer stored on GPU memory.
    /// </summary>
    /// <typeparam name="T">The type of items stored on the buffer.</typeparam>
    [DebuggerTypeProxy(typeof(BufferDebugView<>))]
    [DebuggerDisplay("{ToString(),raw}")]
    public sealed class ConstantBuffer<T> : Buffer<T>
        where T : unmanaged
    {
        /// <summary>
        /// The alignment boundary for elements in a constant buffer.
        /// </summary>
        private const int ElementAlignment = FX.D3D12_COMMONSHADER_CONSTANT_BUFFER_PARTIAL_UPDATE_EXTENTS_BYTE_ALIGNMENT;

        /// <summary>
        /// Creates a new <see cref="ConstantBuffer{T}"/> instance with the specified parameters.
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> associated with the current instance.</param>
        /// <param name="length">The number of items to store in the current buffer.</param>
        /// <param name="allocationMode">The allocation mode to use for the new resource.</param>
        internal ConstantBuffer(GraphicsDevice device, int length, AllocationMode allocationMode)
            : base(device, length, (uint)GetPaddedSize(), ResourceType.Constant, allocationMode)
        {
        }

        /// <summary>
        /// Gets a single <typeparamref name="T"/> value from the current constant buffer.
        /// </summary>
        /// <param name="i">The index of the value to get.</param>
        /// <remarks>This API can only be used from a compute shader, and will always throw if used anywhere else.</remarks>
        public T this[int i] => throw new InvalidExecutionContextException($"{nameof(ConstantBuffer<T>)}<T>[int]");

        /// <summary>
        /// Gets the right padded size for <typeparamref name="T"/> elements to store in the current instance.
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int GetPaddedSize()
        {
            return AlignmentHelper.Pad(sizeof(T), ElementAlignment);
        }

        /// <inheritdoc/>
        internal override unsafe void GetData(ref T destination, int size, int offset)
        {
            GraphicsDevice.ThrowIfDisposed();

            ThrowIfDisposed();

            Guard.IsInRange(offset, 0, Length, nameof(offset));
            Guard.IsLessThanOrEqualTo(offset + size, Length, nameof(size));

            using ID3D12ResourceMap resource = D3D12Resource->Map();

            fixed (void* destinationPointer = &destination)
            {
                if (IsPaddingPresent)
                {
                    MemoryHelper.Copy<T>(
                        resource.Pointer,
                        (uint)offset,
                        (uint)size,
                        (uint)GetPaddedSize(),
                        destinationPointer);
                }
                else
                {
                    MemoryHelper.Copy(
                        resource.Pointer,
                        (uint)offset,
                        (uint)size,
                        (uint)sizeof(T),
                        destinationPointer);
                }
            }
        }

        /// <inheritdoc/>
        internal override unsafe void SetData(ref T source, int size, int offset)
        {
            GraphicsDevice.ThrowIfDisposed();

            ThrowIfDisposed();

            Guard.IsInRange(offset, 0, Length, nameof(offset));
            Guard.IsLessThanOrEqualTo(offset + size, Length, nameof(size));

            using ID3D12ResourceMap resource = D3D12Resource->Map();

            fixed (void* sourcePointer = &source)
            {
                if (IsPaddingPresent)
                {
                    MemoryHelper.Copy<T>(
                        sourcePointer,
                        resource.Pointer,
                        (uint)offset,
                        (uint)size,
                        (uint)GetPaddedSize());
                }
                else
                {
                    MemoryHelper.Copy(
                        sourcePointer,
                        (uint)offset,
                        (uint)size,
                        (uint)sizeof(T),
                        resource.Pointer);
                }
            }
        }

        /// <inheritdoc/>
        public override unsafe void SetData(Buffer<T> source)
        {
            GraphicsDevice.ThrowIfDisposed();

            ThrowIfDeviceMismatch(source.GraphicsDevice);
            ThrowIfDisposed();
            source.ThrowIfDisposed();

            if (source is ConstantBuffer<T> &&
                source.GraphicsDevice == GraphicsDevice)
            {
                // Directly copy the input buffer, if possible
                using CommandList copyCommandList = new(GraphicsDevice, D3D12_COMMAND_LIST_TYPE_COPY);

                copyCommandList.CopyBufferRegion(D3D12Resource, 0, source.D3D12Resource, 0,(ulong)SizeInBytes);
                copyCommandList.ExecuteAndWaitForCompletion();
            }
            else SetDataWithCpuBuffer(source);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ComputeSharp.ConstantBuffer<{typeof(T)}>[{Length}]";
        }
    }
}