// Copyright (c) 2026 Glenn Watson. All rights reserved.
// Glenn Watson licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.Intrinsics;

namespace HyperCodecs.H265;

/// <summary>The explicit weighting of a prediction block (8-275 to 8-277).</summary>
/// <param name="UsesList0">Whether the block predicts from list 0.</param>
/// <param name="UsesList1">Whether the block predicts from list 1.</param>
/// <param name="First">The weight and offset for list 0.</param>
/// <param name="Second">The weight and offset for list 1.</param>
/// <param name="Log2Wd">The shift of the weighting.</param>
internal readonly record struct H265ExplicitWeighting(bool UsesList0, bool UsesList1, H265Weight First, H265Weight Second, int Log2Wd)
{
    /// <summary>Weights the intermediate samples of the lists into one sample.</summary>
    /// <param name="sample0">The sample of list 0.</param>
    /// <param name="sample1">The sample of list 1.</param>
    /// <returns>The weighted sample, before it is clipped.</returns>
    internal int Combine(int sample0, int sample1)
    {
        if (UsesList0 && UsesList1)
        {
            return ((sample0 * First.Weight) + (sample1 * Second.Weight) + ((First.Offset + Second.Offset + 1) << Log2Wd)) >> (Log2Wd + 1);
        }

        return UsesList0 ? Single(sample0, First) : Single(sample1, Second);
    }

    /// <summary>Weights four intermediate samples of each list into four samples, as <see cref="Combine(int, int)"/> does for one.</summary>
    /// <param name="sample0">The samples of list 0.</param>
    /// <param name="sample1">The samples of list 1.</param>
    /// <returns>The weighted samples, before they are clipped.</returns>
    internal Vector128<int> Combine(Vector128<int> sample0, Vector128<int> sample1)
    {
        if (UsesList0 && UsesList1)
        {
            var sum = (sample0 * Vector128.Create(First.Weight)) + (sample1 * Vector128.Create(Second.Weight)) + Vector128.Create((First.Offset + Second.Offset + 1) << Log2Wd);
            return Vector128.ShiftRightArithmetic(sum, Log2Wd + 1);
        }

        return UsesList0 ? SingleVector(sample0, First) : SingleVector(sample1, Second);
    }

    /// <summary>Weights four samples of one list.</summary>
    /// <param name="sample">The intermediate samples.</param>
    /// <param name="weight">The weight and offset.</param>
    /// <returns>The weighted samples.</returns>
    private Vector128<int> SingleVector(Vector128<int> sample, H265Weight weight) =>
        Vector128.ShiftRightArithmetic((sample * Vector128.Create(weight.Weight)) + Vector128.Create(1 << (Log2Wd - 1)), Log2Wd) + Vector128.Create(weight.Offset);

    /// <summary>Weights the samples of one list.</summary>
    /// <param name="sample">The intermediate sample.</param>
    /// <param name="weight">The weight and offset.</param>
    /// <returns>The weighted sample.</returns>
    private int Single(int sample, H265Weight weight) => (((sample * weight.Weight) + (1 << (Log2Wd - 1))) >> Log2Wd) + weight.Offset;
}
