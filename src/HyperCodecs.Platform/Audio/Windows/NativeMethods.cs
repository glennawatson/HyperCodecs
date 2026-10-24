// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace HyperCodecs.Platform.Audio.Windows;

/// <summary>Source generated entry points of the Windows COM functions the sound output uses, all part of Windows itself.</summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Native <c>CoInitializeEx</c>.</summary>
    /// <param name="reserved">Must be null.</param>
    /// <param name="flags">The threading model.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CoInitializeEx(nint reserved, uint flags);

    /// <summary>Native <c>CoUninitialize</c>.</summary>
    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void CoUninitialize();

    /// <summary>Native <c>CoCreateInstance</c>.</summary>
    /// <param name="classId">The class.</param>
    /// <param name="outer">The aggregating object, or null.</param>
    /// <param name="context">Where the object runs.</param>
    /// <param name="interfaceId">The interface wanted.</param>
    /// <param name="instance">Receives the interface.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CoCreateInstance(Guid* classId, void* outer, uint context, Guid* interfaceId, void** instance);
}
