// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;
using Xunit;

namespace ViewfinderHeadTracking.Tests;

internal static class Approx
{
    private const float Tolerance = 1e-4f;

    internal static void Equal(float expected, float actual)
    {
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
    }

    internal static void Equal(Vector3 expected, Vector3 actual)
    {
        Equal(expected.x, actual.x);
        Equal(expected.y, actual.y);
        Equal(expected.z, actual.z);
    }

    internal static void Equal(Vector2 expected, Vector2 actual)
    {
        Equal(expected.x, actual.x);
        Equal(expected.y, actual.y);
    }
}
