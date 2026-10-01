using System.Runtime.CompilerServices;

// Hot paths stackalloc small scratch buffers; they are always written before being read.
[module: SkipLocalsInit]
