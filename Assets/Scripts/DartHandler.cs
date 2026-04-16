using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DartHandler : MonoBehaviour
{
    [SerializeField]
    private GameObject Dart;

    [SerializeField]
    private GameObject Constants;

    [SerializeField]
    private TrailRenderer trail = null;

    [SerializeField]
    private Vector3 velocity = Vector3.zero;

    [SerializeField]
    private Vector3 defaultAcceleration = new Vector3(0, -6, 0);

    private Vector3 acceleration = new Vector3(0, -6, 0);
    private bool stopped = false;
    private bool hasRegisteredBoardHit = false;

    [Header("Training Data")]
    public Vector3 releaseVelocity;
    public float releaseAngle;
    public string releaseFeedback;
    public float releaseStabilityScore;

    void Start()
    {
        acceleration = defaultAcceleration;

        if (Constants != null)
            return;

        List<GameObject> rootObjects = new List<GameObject>();
        Scene scene = SceneManager.GetActiveScene();
        scene.GetRootGameObjects(rootObjects);

        for (int i = 0; i < rootObjects.Count; i++)
        {
            GameObject go = rootObjects[i];
            if (go.name == "Constants")
            {
                Constants = go;
                break;
            }
        }
    }

    public void Pause(bool pause)
    {
        stopped = pause;
        if (trail != null)
            trail.enabled = !pause;
    }

    public void SetVelocity(Vector3 velocity)
    {
        this.velocity = velocity;
        if (trail != null)
            trail.enabled = true;
    }

    public void SetThrowData(Vector3 velocity, float angle, string feedback, float stabilityScore)
    {
        releaseVelocity = velocity;
        releaseAngle = angle;
        releaseFeedback = feedback;
        releaseStabilityScore = stabilityScore;
    }

    void Update()
    {
        if (stopped)
            return;

        float gravityMultiplier = 1f;
        if (Constants != null)
            gravityMultiplier = Constants.GetComponent<ConstantsScript>().Gravity;

        velocity += acceleration * gravityMultiplier * Time.deltaTime;

        int layerMaskCombined =
              (1 << (int)Layers.UI)
            | (1 << (int)Layers.Menu)
            | (1 << (int)Layers.Dart)
            | (1 << (int)Layers.Gravity);

        layerMaskCombined = ~layerMaskCombined;
        Vector3 dir = velocity.normalized;
        Vector3 from = transform.position + dir * 0.05f;

        RaycastHit hit;
        if (Physics.Raycast(from, dir, out hit, (velocity * Time.deltaTime).magnitude, layerMaskCombined))
        {
            if (hit.collider.gameObject.name != "Dartboard")
                Debug.Log(hit.collider.gameObject.name);

            transform.position = hit.point;
            velocity = Vector3.zero;
            stopped = true;
        }
        else
        {
            transform.position += velocity * Time.deltaTime;
        }

        if (velocity.magnitude > 0.01f)
            transform.forward = velocity.normalized;
    }

    void OnTriggerEnter(Collider collision)
    {
        if (collision.CompareTag("Menu"))
            return;

        if (collision.CompareTag("Gravity"))
        {
            GravityField field = collision.gameObject.GetComponent<GravityField>();
            if (field != null)
                acceleration = field.getGravity();
            return;
        }

        if (stopped)
            return;

        transform.position += velocity.normalized * 0.007f;
        velocity = Vector3.zero;
        stopped = true;

        if (collision.CompareTag("Board") && !hasRegisteredBoardHit)
        {
            hasRegisteredBoardHit = true;
            transform.parent = collision.gameObject.transform;

            BoardHandler board = collision.gameObject.GetComponent<BoardHandler>();
            if (board != null)
                board.hit(gameObject);
        }
    }
}
