using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;

public enum Gesture
{
    None,
    Pinch,
    Grab,
    Peace,
    OpenPalm,
    ThumbsUp
}

public class GestureHandler : MonoBehaviour
{
    private Gesture currentGesture = Gesture.None;
    private Gesture fixedGesture = Gesture.None;

    private float timeNewGesture = 0.12f;
    private float gestureStart = 0.0f;

    [SerializeField]
    private Handedness handedness = Handedness.None;

    void Start()
    {
    }

    public Gesture GetGesture()
    {
        return fixedGesture;
    }

    void FixedUpdate()
    {
        Gesture newGesture = Gesture.None;

        if (HandJointUtils.FindHand(handedness) != null)
        {
            if (GestureUtils.IsThumbsUp(handedness))
                newGesture = Gesture.ThumbsUp;
            else if (GestureUtils.IsPeaceSign(handedness))
                newGesture = Gesture.Peace;
            else if (GestureUtils.IsOpenPalm(handedness))
                newGesture = Gesture.OpenPalm;
            else if (GestureUtils.IsPinching(handedness))
                newGesture = Gesture.Pinch;
            else if (GestureUtils.IsGrabbing(handedness))
                newGesture = Gesture.Grab;
        }
            
        if (newGesture != currentGesture)
        {
            gestureStart = Time.time;
            currentGesture = newGesture;
        }

        if (fixedGesture != newGesture)
        {
            if (Time.time - gestureStart > timeNewGesture)
            {
                fixedGesture = newGesture;
            }
        }
    }
}