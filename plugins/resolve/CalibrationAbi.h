#pragma once
// ABI version 1. See Exports.cs. Never unload NativeAOT while the Resolve process is alive.
using CalibrationAbi = int (*)();
using AnalyzeSingle = int (*)(const float*, int, int, int, int, int, int,
    const double*, double*, char*, int);
