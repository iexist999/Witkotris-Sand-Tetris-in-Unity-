using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArrayHelper
{
    public static Vector2Int[] Shuffle(Vector2Int[] array)
    {
        if (array is null)
            throw new ArgumentNullException(nameof(array));

        for (int i = 0; i < array.Length - 1; ++i)
        {
            int r = UnityEngine.Random.Range(i, array.Length);  // Unity's Random.Range for index selection
            // Swap Vector2Int elements
            Vector2Int temp = array[r];
            array[r] = array[i];
            array[i] = temp;
        }
        return array;  // Return the shuffled array
    }
}