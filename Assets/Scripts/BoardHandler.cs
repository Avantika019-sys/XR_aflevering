using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

[System.Serializable]
public class ThrowRecord
{
    public int throwIndex;
    public int score;
    public float hitX;
    public float hitY;
    public float releaseAngle;
    public float releaseSpeed;
    public float stability;
    public string mistake;
    public string confidence;
    public string tip;
    public string adaptiveHint;

    public ThrowRecord(
        int throwIndex,
        int score,
        float hitX,
        float hitY,
        float releaseAngle,
        float releaseSpeed,
        float stability,
        string mistake,
        string confidence,
        string tip,
        string adaptiveHint)
    {
        this.throwIndex = throwIndex;
        this.score = score;
        this.hitX = hitX;
        this.hitY = hitY;
        this.releaseAngle = releaseAngle;
        this.releaseSpeed = releaseSpeed;
        this.stability = stability;
        this.mistake = mistake;
        this.confidence = confidence;
        this.tip = tip;
        this.adaptiveHint = adaptiveHint;
    }
}

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
    private Vector3 baseBoardScale = Vector3.one;
    private bool hasCachedBoardScale = false;

    public string LastThrowSummary { get; private set; } = "Throw feedback appears after the first hit.";
    public string LastCoachTip { get; private set; } = "Throw to get a coaching tip.";
    public string LastAdaptiveAimHint { get; private set; } = "Collecting throw pattern...";

    [Header("Mistake Classifier Thresholds")]
    [SerializeField]
    private float tooFlatAngle = 7f;
    [SerializeField]
    private float tooSteepAngle = 20f;
    [SerializeField]
    private float horizontalBiasThreshold = 0.03f;
    [SerializeField]
    private float verticalBiasThreshold = 0.03f;
    [SerializeField]
    private float unstableThreshold = 0.45f;

    [Header("Analytics Windows")]
    [SerializeField]
    private int adaptiveHintWindow = 8;
    [SerializeField]
    private int speedHistoryWindow = 8;
    [SerializeField]
    private int maxThrowHistory = 30;

    private readonly List<int> points = new List<int>();
    private readonly List<ThrowRecord> throwHistory = new List<ThrowRecord>();
    private readonly List<float> recentHitXs = new List<float>();
    private readonly List<float> recentHitYs = new List<float>();
    private readonly List<float> recentReleaseSpeeds = new List<float>();

    void Start()
    {
        CacheBoardScale();
        ApplyBoardSize();
        projectTo(Vector3.zero, Vector3.forward);
    }

    public void hit(GameObject obj)
    {
        int layerMaskCombined = (1 << (int)Layers.Board);
        RaycastHit boardHit;

        if (obj == null)
        {
            Debug.LogWarning("BoardHandler.hit called with null dart object.");
            return;
        }

        Vector3 dir = obj.transform.forward;
        Vector3 from = obj.transform.position - dir * 2.0f;

        if (!Physics.Raycast(from, dir, out boardHit, 10f, layerMaskCombined))
        {
            Debug.Log("Ray did not hit board");
            return;
        }

        Vector3 brd = transform.InverseTransformPoint(boardHit.point);
        Vector3 scl = transform.localScale;
        brd = new Vector3(
            brd.x / Mathf.Max(0.0001f, scl.x),
            brd.y / Mathf.Max(0.0001f, scl.y),
            brd.z / Mathf.Max(0.0001f, scl.z));

        float hitX = -brd.x;
        float hitY = brd.y;

        int score = calculatePoints(hitX, hitY);
        points.Add(score);

        string mistake = "No throw analytics";
        string confidence = "Low";
        string tip = "Throw data mangler for dette dart.";

        float releaseAngle = 0f;
        float releaseSpeed = 0f;
        float stabilityScore = 0f;

        DartHandler dartHandler = obj.GetComponent<DartHandler>();
        if (dartHandler != null)
        {
            releaseAngle = dartHandler.releaseAngle;
            releaseSpeed = dartHandler.releaseVelocity.magnitude;
            stabilityScore = Mathf.Clamp01(dartHandler.releaseStabilityScore);

            mistake = ClassifyMistake(
                releaseAngle,
                releaseSpeed,
                hitX,
                hitY,
                stabilityScore,
                score,
                out confidence,
                out tip);

            UpdateHistory(hitX, hitY, releaseSpeed, true);
        }
        else
        {
            UpdateHistory(hitX, hitY, 0f, false);
        }

        LastAdaptiveAimHint = GetAdaptiveAimHint();
        LastCoachTip = tip;
        LastThrowSummary =
            "LAST THROW\n" +
            $"{mistake} ({confidence})\n" +
            $"Score: {score} | Stability: {stabilityScore:F2}\n" +
            $"Tip: {tip}\n" +
            $"Aim Assist: {LastAdaptiveAimHint}";

        AddThrowRecord(new ThrowRecord(
            points.Count,
            score,
            hitX,
            hitY,
            releaseAngle,
            releaseSpeed,
            stabilityScore,
            mistake,
            confidence,
            tip,
            LastAdaptiveAimHint));

        Debug.Log($"[TRAINING] Board score: {score}");
        Debug.Log($"[TRAINING] Hit point local: x={hitX:F3}, y={hitY:F3}");
        Debug.Log($"[TRAINING] Classifier: {mistake} ({confidence})");
        Debug.Log($"[TRAINING] Coach tip: {tip}");
        Debug.Log($"[TRAINING] Adaptive hint: {LastAdaptiveAimHint}");
        Debug.Log($"[TRAINING] Total throws recorded: {points.Count}");
    }

    private void AddThrowRecord(ThrowRecord record)
    {
        throwHistory.Add(record);
        if (throwHistory.Count > maxThrowHistory)
            throwHistory.RemoveAt(0);
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
        int sector = Mathf.FloorToInt(((phi + 9f) % 360f) / 18f);
        sector = Mathf.Clamp(sector, 0, pointsBoard.Length - 1);
        int score = pointsBoard[sector];

        if (r < bullsInside / 2f)
        {
            score = 50;
        }
        else if (bullsInside / 2f < r && r < bullsEyeDiameter / 2f)
        {
            score = 25;
        }
        else if (insideRingRadius - tripleDouble < r && r < insideRingRadius)
        {
            score *= 3;
        }
        else if (boardInsideRadius - tripleDouble < r && r < boardInsideRadius)
        {
            score *= 2;
        }
        else if (boardInsideRadius < r)
        {
            score = 0;
        }

        return score;
    }

    private struct PolarCoordinate
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
        float phi = Mathf.Rad2Deg * Mathf.Atan2(y, x);
        if (phi < 0f)
            phi += 360f;

        return new PolarCoordinate(r, phi);
    }

    public bool projectTo(Vector3 from, Vector3 dir)
    {
        ApplyBoardSize();

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
            if (Board != null)
            {
                Board.transform.position = hit.point;
                Board.transform.forward = hit.normal.normalized;
            }

            if (Line != null)
            {
                RaycastHit lineHit;
                Vector3 horizontalBoardNormal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                Vector3 downDir = Vector3.down;

                if (Physics.Raycast(hit.point + playerDistance * horizontalBoardNormal, downDir, out lineHit, 10f, layerMaskCombined))
                {
                    Line.transform.position = lineHit.point;
                    Line.transform.forward = horizontalBoardNormal;
                }
            }

            return true;
        }

        if (Board != null)
        {
            Vector3 safeDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            Board.transform.position = from + safeDirection * 10f;
            Board.transform.forward = -safeDirection;
        }

        return false;
    }

    private void CacheBoardScale()
    {
        if (hasCachedBoardScale || Board == null)
            return;

        baseBoardScale = Board.transform.localScale;
        hasCachedBoardScale = true;
    }

    private void ApplyBoardSize()
    {
        if (Board == null)
            return;

        CacheBoardScale();

        float safeDiameter = Mathf.Max(0.0001f, boardMaxDiameter);
        float scaleFactor = Mathf.Max(0.05f, size / safeDiameter);
        Board.transform.localScale = baseBoardScale * scaleFactor;
    }

    public void SetBoardSize(float newBoardDiameterMeters)
    {
        size = Mathf.Max(0.05f, newBoardDiameterMeters);
        ApplyBoardSize();
    }

    public int GetPoints()
    {
        int total = 0;
        for (int i = 0; i < points.Count; i++)
            total += points[i];
        return total;
    }

    public int GetTotalThrows()
    {
        return points.Count;
    }

    public int GetHitCount()
    {
        int hits = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] > 0)
                hits++;
        }

        return hits;
    }

    public float GetHitRate()
    {
        if (points.Count == 0)
            return 0f;

        return (float)GetHitCount() / points.Count;
    }

    public float GetAverageScore()
    {
        if (points.Count == 0)
            return 0f;

        return points.Average();
    }

    public float GetAverageStability()
    {
        if (throwHistory.Count == 0)
            return 0f;

        return throwHistory.Average(item => item.stability);
    }

    public IReadOnlyList<ThrowRecord> GetThrowHistory()
    {
        return throwHistory;
    }

    public List<ThrowRecord> GetRecentThrows(int maxCount)
    {
        int take = Mathf.Min(Mathf.Max(maxCount, 0), throwHistory.Count);
        List<ThrowRecord> result = new List<ThrowRecord>(take);

        for (int i = throwHistory.Count - 1; i >= throwHistory.Count - take; i--)
            result.Add(throwHistory[i]);

        return result;
    }

    public string GetAnalyticsPanelText(int recentThrowCount)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("THROW ANALYSIS");
        builder.Append($"\nThrows: {GetTotalThrows()}  Hits: {GetHitCount()} ({GetHitRate() * 100f:F0}%)");
        builder.Append($"\nAvg Score/Throw: {GetAverageScore():F2}");
        builder.Append($"\nAvg Stability: {GetAverageStability():F2}");
        builder.Append($"\nBias Trend: {GetBiasDescription()}");
        builder.Append($"\nAim Assist Hint: {LastAdaptiveAimHint}");

        if (throwHistory.Count == 0)
        {
            builder.Append("\n\nNo throws recorded yet.");
            builder.Append($"\n\nCoach Tip: {LastCoachTip}");
            return builder.ToString();
        }

        builder.Append("\n\nRECENT THROWS");
        List<ThrowRecord> recentThrows = GetRecentThrows(recentThrowCount);
        for (int i = 0; i < recentThrows.Count; i++)
        {
            ThrowRecord item = recentThrows[i];
            builder.Append(
                $"\n#{item.throwIndex}  S:{item.score}  STB:{item.stability:F2}  {item.mistake} ({item.confidence})");
        }

        builder.Append($"\n\nCoach Tip: {LastCoachTip}");
        return builder.ToString();
    }

    public void ResetPoints()
    {
        points.Clear();
        ResetAnalytics();
    }

    public void ResetAnalytics()
    {
        LastThrowSummary = "Throw feedback appears after the first hit.";
        LastCoachTip = "Throw to get a coaching tip.";
        LastAdaptiveAimHint = "Collecting throw pattern...";

        throwHistory.Clear();
        recentHitXs.Clear();
        recentHitYs.Clear();
        recentReleaseSpeeds.Clear();
    }

    private void UpdateHistory(float hitX, float hitY, float releaseSpeed, bool includeReleaseSpeed)
    {
        recentHitXs.Add(hitX);
        if (recentHitXs.Count > adaptiveHintWindow)
            recentHitXs.RemoveAt(0);

        recentHitYs.Add(hitY);
        if (recentHitYs.Count > adaptiveHintWindow)
            recentHitYs.RemoveAt(0);

        if (includeReleaseSpeed)
        {
            recentReleaseSpeeds.Add(releaseSpeed);
            if (recentReleaseSpeeds.Count > speedHistoryWindow)
                recentReleaseSpeeds.RemoveAt(0);
        }
    }

    private string ClassifyMistake(
        float releaseAngle,
        float releaseSpeed,
        float hitX,
        float hitY,
        float stabilityScore,
        int score,
        out string confidence,
        out string tip)
    {
        if (stabilityScore < unstableThreshold)
        {
            float certainty = Mathf.Clamp01((unstableThreshold - stabilityScore) / Mathf.Max(0.0001f, unstableThreshold));
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

        if (hitY < -verticalBiasThreshold)
        {
            float certainty = Mathf.Clamp01(Mathf.Abs(hitY + verticalBiasThreshold) / 0.08f);
            confidence = ConfidenceFromValue(certainty);
            tip = "Raise release line slightly and follow through higher.";
            return "Low release line";
        }

        if (hitY > verticalBiasThreshold)
        {
            float certainty = Mathf.Clamp01(Mathf.Abs(hitY - verticalBiasThreshold) / 0.08f);
            confidence = ConfidenceFromValue(certainty);
            tip = "Lower release line slightly and keep wrist neutral.";
            return "High release line";
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

        if (score == 0)
        {
            confidence = "Low";
            tip = "Keep elbow stable and align dart to center before release.";
            return "Off target";
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
        if (recentHitXs.Count < 4 || recentHitYs.Count < 4)
            return "Collecting throw pattern...";

        float meanHitX = recentHitXs.Average();
        float meanHitY = recentHitYs.Average();

        List<string> adjustments = new List<string>();

        if (meanHitX < -0.02f)
            adjustments.Add("right");
        else if (meanHitX > 0.02f)
            adjustments.Add("left");

        if (meanHitY < -0.02f)
            adjustments.Add("up");
        else if (meanHitY > 0.02f)
            adjustments.Add("down");

        if (adjustments.Count == 0)
            return "Aim centered. Keep current alignment.";

        if (adjustments.Count == 1)
            return $"Trend detected. Shift aim slightly {adjustments[0]}.";

        return $"Trend detected. Shift aim {adjustments[0]} and {adjustments[1]}.";
    }

    private string GetBiasDescription()
    {
        if (recentHitXs.Count < 4 || recentHitYs.Count < 4)
            return "Collecting pattern...";

        float meanHitX = recentHitXs.Average();
        float meanHitY = recentHitYs.Average();

        string horizontal = "Centered";
        if (meanHitX < -0.02f)
            horizontal = "Left";
        else if (meanHitX > 0.02f)
            horizontal = "Right";

        string vertical = "Center";
        if (meanHitY < -0.02f)
            vertical = "Low";
        else if (meanHitY > 0.02f)
            vertical = "High";

        return $"{horizontal}/{vertical}";
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
