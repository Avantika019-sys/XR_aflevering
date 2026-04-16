using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class BoardHandler : MonoBehaviour
{
    public GameObject Board;
    public GameObject Line;

    public float size = 0.45f;
    public float playerDistance = 2.37f;
    public float boardMaxDiameter = 0.451f;
    public float boardInsideRadius = 0.165f;
    public float bullsEyeDiameter = 0.032f;
    public float bullsInside = 0.0127f;
    public float insideRingRadius = 0.102f;
    public float tripleDouble = 0.008f;

    public List<int> points = new List<int>();
    public string LastThrowSummary { get; private set; } = "Throw feedback appears after the first hit.";

    [Header("Mistake Classifier Thresholds")]
    [SerializeField]
    private float tooFlatAngle = 7f;
    [SerializeField]
    private float tooSteepAngle = 20f;
    [SerializeField]
    private float horizontalBiasThreshold = 0.03f;
    [SerializeField]
    private float unstableThreshold = 0.45f;
    [SerializeField]
    private int adaptiveHintWindow = 8;
    [SerializeField]
    private int speedHistoryWindow = 8;

    private List<float> recentHitXs = new List<float>();
    private List<float> recentReleaseSpeeds = new List<float>();

    void Start()
    {
        projectTo(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f));
    }

    void Update()
    {
    }

    public void hit(GameObject obj)
    {
        int layerMaskCombined = (1 << (int)Layers.Board);
        RaycastHit hit;

        Vector3 dir = obj.transform.forward;
        Vector3 from = obj.transform.position - dir * 2.0f;

        if (Physics.Raycast(from, dir, out hit, 10, layerMaskCombined))
        {
            Vector3 brd = transform.InverseTransformPoint(hit.point);
            Vector3 scl = transform.localScale;
            brd = new Vector3(brd.x / scl.x, brd.y / scl.y, brd.z / scl.z);

            int score = calculatePoints(-brd.x, brd.y);
            points.Add(score);

            Debug.Log($"[TRAINING] Board score: {score}");
            Debug.Log($"[TRAINING] Hit point local: x={-brd.x:F3}, y={brd.y:F3}");

            DartHandler dartHandler = obj.GetComponent<DartHandler>();
            if (dartHandler != null)
            {
                Debug.Log($"[TRAINING] Release angle at hit: {dartHandler.releaseAngle:F2} degrees");
                Debug.Log($"[TRAINING] Feedback at release: {dartHandler.releaseFeedback}");
                Debug.Log($"[TRAINING] Release velocity at hit: {dartHandler.releaseVelocity}");
                Debug.Log($"[TRAINING] Stability score at release: {dartHandler.releaseStabilityScore:F2}");

                string confidence;
                string tip;
                string mistake = ClassifyMistake(
                    dartHandler.releaseAngle,
                    dartHandler.releaseVelocity.magnitude,
                    -brd.x,
                    dartHandler.releaseStabilityScore,
                    score,
                    out confidence,
                    out tip);

                UpdateHistory(-brd.x, dartHandler.releaseVelocity.magnitude);
                string adaptiveAimHint = GetAdaptiveAimHint();

                LastThrowSummary =
                    $"LAST THROW\n" +
                    $"{mistake} ({confidence})\n" +
                    $"Tip: {tip}\n" +
                    $"Aim Assist: {adaptiveAimHint}";

                Debug.Log($"[TRAINING] Classifier: {mistake} ({confidence})");
                Debug.Log($"[TRAINING] Coach tip: {tip}");
                Debug.Log($"[TRAINING] Adaptive hint: {adaptiveAimHint}");
            }
            else 
            {
                LastThrowSummary = "No throw analytics available for this dart.";
            }

            Debug.Log($"[TRAINING] Total throws recorded: {points.Count}");
        }
        else
        {
            Debug.Log("Ray did not hit board");
        }
    }

    private int calculatePoints(float x, float y)
    {
        return calculatePoints(cartesianToPolar(x, y));
    }

    private int calculatePoints(PolarCoordinate pos)
    {
        float r = pos.r;
        float phi = pos.phi;
        int[] pointsBoard = new int[] { 6, 13, 4, 18, 1, 20, 5, 12, 9, 14, 11, 8, 16, 7, 19, 3, 17, 2, 15, 10 };
        int points = pointsBoard[Mathf.FloorToInt(((phi + 9) % 360) / 18)];

        if (r < bullsInside / 2)
        {
            points = 50;
        }
        else if (bullsInside / 2 < r && r < bullsEyeDiameter / 2)
        {
            points = 25;
        }
        else if (insideRingRadius - tripleDouble < r && r < insideRingRadius)
        {
            points *= 3;
        }
        else if (boardInsideRadius - tripleDouble < r && r < boardInsideRadius)
        {
            points *= 2;
        }
        else if (boardInsideRadius < r)
        {
            points = 0;
        }

        return points;
    }

    struct PolarCoordinate
    {
        public float r;
        public float phi;

        public PolarCoordinate(float r, float phi)
        {
            this.r = r;
            this.phi = phi;
        }
    }

    private PolarCoordinate cartesianToPolar(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float phi = 0f;

        if (x == 0)
        {
            if (y > 0)
                phi = 90f;
            else
                phi = 270f;
        }
        else
        {
            phi = Mathf.Rad2Deg * Mathf.Atan(y / x);

            if (x < 0)
                phi += 180;
            else if (y < 0)
                phi += 360;
        }

        return new PolarCoordinate(r, phi);
    }

    public bool projectTo(Vector3 from, Vector3 dir)
    {
        int layerMaskCombined =
              (1 << (int)Layers.UI)
            | (1 << (int)Layers.Menu)
            | (1 << (int)Layers.Board)
            | (1 << (int)Layers.Dart)
            | (1 << (int)Layers.Gravity);

        layerMaskCombined = ~layerMaskCombined;

        RaycastHit hit;
        if (Physics.Raycast(from, dir, out hit, 10f, layerMaskCombined))
        {
            Board.transform.position = hit.point;
            Board.transform.forward = hit.normal.normalized;

            RaycastHit lineHit;
            Vector3 horizontalBoardNormal = new Vector3(hit.normal.x, 0f, hit.normal.z);
            horizontalBoardNormal = horizontalBoardNormal.normalized;
            Vector3 downDir = new Vector3(0f, -1f, 0f);

            if (Physics.Raycast(hit.point + playerDistance * horizontalBoardNormal, downDir, out lineHit, 10f, layerMaskCombined))
            {
                Line.transform.position = lineHit.point;
                Line.transform.forward = horizontalBoardNormal;
            }

            return true;
        }
        else
        {
            Board.transform.position = dir * 10;
            Board.transform.forward = -dir;
            return false;
        }
    }

    public int GetPoints()
    {
        int counter = 0;
        for (int i = 0; i < points.Count; i++)
        {
            counter += points[i];
        }
        return counter;
    }

    public void ResetPoints()
    {
        points = new List<int>();
        ResetAnalytics();
    }

    public void ResetAnalytics()
    {
        LastThrowSummary = "Throw feedback appears after the first hit.";
        recentHitXs.Clear();
        recentReleaseSpeeds.Clear();
    }

    private void UpdateHistory(float hitX, float releaseSpeed)
    {
        recentHitXs.Add(hitX);
        if (recentHitXs.Count > adaptiveHintWindow)
            recentHitXs.RemoveAt(0);

        recentReleaseSpeeds.Add(releaseSpeed);
        if (recentReleaseSpeeds.Count > speedHistoryWindow)
            recentReleaseSpeeds.RemoveAt(0);
    }

    private string ClassifyMistake(
        float releaseAngle,
        float releaseSpeed,
        float hitX,
        float stabilityScore,
        int score,
        out string confidence,
        out string tip)
    {
        if (stabilityScore < unstableThreshold)
        {
            float certainty = Mathf.Clamp01((unstableThreshold - stabilityScore) / unstableThreshold);
            confidence = ConfidenceFromValue(certainty);
            tip = "Hold pinch 100-150 ms longer before release.";
            return "Unstable release";
        }

        if (releaseAngle < tooFlatAngle)
        {
            float certainty = Mathf.Clamp01((tooFlatAngle - releaseAngle) / Mathf.Max(tooFlatAngle, 0.001f));
            confidence = ConfidenceFromValue(certainty);
            tip = "Lift wrist slightly to increase arc.";
            return "Too flat";
        }

        if (releaseAngle > tooSteepAngle)
        {
            float certainty = Mathf.Clamp01((releaseAngle - tooSteepAngle) / Mathf.Max(tooSteepAngle, 0.001f));
            confidence = ConfidenceFromValue(certainty);
            tip = "Lower release angle by around 3-5 degrees.";
            return "Too steep";
        }

        if (hitX < -horizontalBiasThreshold)
        {
            float certainty = Mathf.Clamp01(Mathf.Abs(hitX + horizontalBiasThreshold) / 0.08f);
            confidence = ConfidenceFromValue(certainty);
            tip = "Aim a little more to the right before release.";
            return "Left bias";
        }

        if (hitX > horizontalBiasThreshold)
        {
            float certainty = Mathf.Clamp01(Mathf.Abs(hitX - horizontalBiasThreshold) / 0.08f);
            confidence = ConfidenceFromValue(certainty);
            tip = "Aim a little more to the left before release.";
            return "Right bias";
        }

        if (IsUnderpoweredThrow(releaseSpeed, score))
        {
            confidence = "Medium";
            tip = "Accelerate hand forward a bit more in the last 10 cm.";
            return "Underpowered throw";
        }

        confidence = "High";
        tip = "Good mechanics. Keep the same timing.";
        return "Good throw";
    }

    private bool IsUnderpoweredThrow(float releaseSpeed, int score)
    {
        if (recentReleaseSpeeds.Count < 3)
            return false;

        float averageReleaseSpeed = recentReleaseSpeeds.Average();
        if (averageReleaseSpeed < 0.0001f)
            return false;

        return score == 0 && releaseSpeed < averageReleaseSpeed * 0.7f;
    }

    private string GetAdaptiveAimHint()
    {
        if (recentHitXs.Count < 4)
            return "Collecting throw pattern...";

        float meanHitX = recentHitXs.Average();

        if (meanHitX < -0.02f)
            return "Trend left. Shift aim right by ~2 cm.";
        if (meanHitX > 0.02f)
            return "Trend right. Shift aim left by ~2 cm.";

        return "Aim centered. Keep current alignment.";
    }

    private string ConfidenceFromValue(float value)
    {
        if (value > 0.66f)
            return "High";
        if (value > 0.33f)
            return "Medium";
        return "Low";
    }
}
