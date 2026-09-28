using UnityEngine;
using Tom.PathCreator;

public sealed class PathFollowerExample : MonoBehaviour
{
    [SerializeField]
    private PathCreator path;

    [SerializeField, Min(0f)]
    private float speed = 3f;

    private float distanceTime;

    private void Update()
    {
        if (path == null || speed <= 0f)
        {
            return;
        }

        distanceTime = Mathf.Repeat(distanceTime + Time.deltaTime * speed * 0.05f, 1f);
        transform.position = path.EvaluatePosition(distanceTime);

        Vector3 tangent = path.EvaluateTangent(distanceTime);
        if (tangent.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }
    }
}
