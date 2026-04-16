using Microsoft.MixedReality.Toolkit.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GestureUtils
{
    private const float PinchThresholdPlacement = 0.7f;
    private const float PinchThresholdThrowing = 0.2f;
    private const float GrabThreshold = 0.4f;

    private static InputController cachedInputController = null;

    public static bool IsPinching(Handedness trackedHand)
    {
        InputController inputController = GetInputController();

        float currentThreshold = PinchThresholdPlacement;
        if (inputController != null &&
            (inputController.mode == Modes.Dart || inputController.mode == Modes.Idle))
        {
            currentThreshold = PinchThresholdThrowing;
        }

        return HandPoseUtils.CalculateIndexPinch(trackedHand) > currentThreshold;
    }

    public static bool IsGrabbing(Handedness trackedHand)
    {
        return !IsPinching(trackedHand) &&
               HandPoseUtils.MiddleFingerCurl(trackedHand) > GrabThreshold &&
               HandPoseUtils.RingFingerCurl(trackedHand) > GrabThreshold &&
               HandPoseUtils.PinkyFingerCurl(trackedHand) > GrabThreshold &&
               HandPoseUtils.ThumbFingerCurl(trackedHand) > GrabThreshold;
    }

    private static InputController GetInputController()
    {
        if (cachedInputController != null)
            return cachedInputController;

        Scene scene = SceneManager.GetActiveScene();
        GameObject[] rootObjects = scene.GetRootGameObjects();

        for (int i = 0; i < rootObjects.Length; i++)
        {
            GameObject go = rootObjects[i];
            if (go.name != "Constants")
                continue;

            ConstantsScript constants = go.GetComponent<ConstantsScript>();
            if (constants == null || constants.InputController == null)
                return null;

            cachedInputController = constants.InputController.GetComponent<InputController>();
            return cachedInputController;
        }

        return null;
    }
}
