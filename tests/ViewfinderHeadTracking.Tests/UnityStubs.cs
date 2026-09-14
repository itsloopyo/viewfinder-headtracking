// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

// The slice of UnityEngine the files under test use, with Unity's own semantics for
// the edge cases they depend on: Vector3 equality within 1e-5, Normalize to zero
// below 1e-5, ProjectOnPlane returning the vector for a degenerate normal, and
// Mathf.Sign(0) = 1.

namespace UnityEngine;

public static class Mathf
{
    public const float Deg2Rad = (float)(System.Math.PI / 180.0);
    public const float Rad2Deg = (float)(180.0 / System.Math.PI);
    public static readonly float Epsilon = float.Epsilon;

    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    public static float Tan(float radians) => (float)System.Math.Tan(radians);
    public static float Atan(float v) => (float)System.Math.Atan(v);
    public static float Sqrt(float v) => (float)System.Math.Sqrt(v);
    public static float Abs(float v) => System.Math.Abs(v);
    public static float Max(float a, float b) => a > b ? a : b;
    public static float Sign(float v) => v >= 0f ? 1f : -1f;
}

public struct Vector2
{
    public float x;
    public float y;

    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    public static Vector2 zero => new(0f, 0f);

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
}

public struct Vector3
{
    private const float EqualityEpsilon = 1e-5f;

    public float x;
    public float y;
    public float z;

    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static Vector3 zero => new(0f, 0f, 0f);
    public static Vector3 one => new(1f, 1f, 1f);
    public static Vector3 up => new(0f, 1f, 0f);
    public static Vector3 right => new(1f, 0f, 0f);
    public static Vector3 forward => new(0f, 0f, 1f);

    public float sqrMagnitude => x * x + y * y + z * z;
    public float magnitude => Mathf.Sqrt(sqrMagnitude);

    public void Normalize()
    {
        float m = magnitude;
        this = m > EqualityEpsilon ? this / m : zero;
    }

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);
    public static Vector3 operator *(Vector3 a, float s) => new(a.x * s, a.y * s, a.z * s);
    public static Vector3 operator *(float s, Vector3 a) => a * s;
    public static Vector3 operator /(Vector3 a, float s) => new(a.x / s, a.y / s, a.z / s);
    public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < EqualityEpsilon * EqualityEpsilon;
    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

    public override bool Equals(object? other) => other is Vector3 v && x == v.x && y == v.y && z == v.z;
    public override int GetHashCode() => System.HashCode.Combine(x, y, z);
    public override string ToString() => $"({x:F5}, {y:F5}, {z:F5})";

    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

    public static Vector3 Cross(Vector3 a, Vector3 b) =>
        new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

    public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
    {
        float sqrMag = Dot(planeNormal, planeNormal);
        if (sqrMag < Mathf.Epsilon) return vector;
        return vector - planeNormal * (Dot(vector, planeNormal) / sqrMag);
    }
}

public struct Quaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public Quaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public static Quaternion identity => new(0f, 0f, 0f, 1f);

    public static Quaternion operator *(Quaternion a, Quaternion b) => new(
        a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
        a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
        a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
        a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

    public static Quaternion Inverse(Quaternion q) => new(-q.x, -q.y, -q.z, q.w);

    /// <summary>Unity's order: about z, then x, then y.</summary>
    public static Quaternion Euler(float xDegrees, float yDegrees, float zDegrees)
    {
        return AngleAxis(yDegrees, Vector3.up) * AngleAxis(xDegrees, Vector3.right) * AngleAxis(zDegrees, Vector3.forward);
    }

    public static Quaternion AngleAxis(float angleDegrees, Vector3 axis)
    {
        axis.Normalize();
        float half = angleDegrees * Mathf.Deg2Rad * 0.5f;
        float s = (float)System.Math.Sin(half);
        return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)System.Math.Cos(half));
    }

    public static Vector3 operator *(Quaternion q, Vector3 v)
    {
        float tx = 2f * (q.y * v.z - q.z * v.y);
        float ty = 2f * (q.z * v.x - q.x * v.z);
        float tz = 2f * (q.x * v.y - q.y * v.x);
        return new Vector3(
            v.x + q.w * tx + (q.y * tz - q.z * ty),
            v.y + q.w * ty + (q.z * tx - q.x * tz),
            v.z + q.w * tz + (q.x * ty - q.y * tx));
    }
}

// Compile-time surface only. The files that use these are compiled in for their pure
// helpers; nothing under test reaches the engine, and a test that did would fail loudly.

public struct Vector4
{
    public float x;
    public float y;
    public float z;
    public float w;

    public Vector4(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }
}

public struct Matrix4x4
{
    public float m20;
    public float m21;
    public float m22;
    public float m23;

    public static Matrix4x4 identity => throw new System.NotSupportedException();
    public Matrix4x4 inverse => throw new System.NotSupportedException();
    public static Matrix4x4 TRS(Vector3 position, Quaternion rotation, Vector3 scale) => throw new System.NotSupportedException();
    public static Matrix4x4 Rotate(Quaternion rotation) => throw new System.NotSupportedException();
    public static Matrix4x4 Translate(Vector3 translation) => throw new System.NotSupportedException();
    public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => throw new System.NotSupportedException();
    public static Vector4 operator *(Matrix4x4 m, Vector4 v) => throw new System.NotSupportedException();
}

public class Camera
{
    public Matrix4x4 projectionMatrix => throw new System.NotSupportedException();
    public int pixelWidth => throw new System.NotSupportedException();
    public int pixelHeight => throw new System.NotSupportedException();
}

public class GameObject
{
    public int layer => throw new System.NotSupportedException();
}

public class Collider
{
    public GameObject gameObject => throw new System.NotSupportedException();
}

public struct RaycastHit
{
    public Vector3 point => throw new System.NotSupportedException();
    public Collider collider => throw new System.NotSupportedException();
}

public enum QueryTriggerInteraction
{
    UseGlobal,
    Ignore,
    Collide
}

public static class Physics
{
    public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask,
        QueryTriggerInteraction queryTriggerInteraction) => throw new System.NotSupportedException();
}
