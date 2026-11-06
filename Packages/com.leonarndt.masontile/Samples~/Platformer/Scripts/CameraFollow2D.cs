using UnityEngine;
using UnityEngine.Tilemaps;

namespace MasonTile.Samples.Platformer
{
    /// <summary>Smoothly follows a target and keeps the view inside a tilemap's bounds.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : MonoBehaviour
    {
        public Transform target;
        public Tilemap bounds;
        public float smoothTime = 0.12f;

        Camera m_Camera;
        Vector3 m_Velocity;

        void Awake() => m_Camera = GetComponent<Camera>();

        void Start() => Snap();

        /// <summary>Jumps straight to the target, e.g. after a respawn or level change.</summary>
        public void Snap()
        {
            if (target == null) return;
            transform.position = Clamp(target.position);
            m_Velocity = Vector3.zero;
        }

        void LateUpdate()
        {
            if (target == null) return;
            transform.position = Vector3.SmoothDamp(transform.position, Clamp(target.position), ref m_Velocity, smoothTime);
        }

        Vector3 Clamp(Vector3 p)
        {
            var result = new Vector3(p.x, p.y, transform.position.z);
            if (bounds == null || !m_Camera.orthographic) return result;

            var b = bounds.localBounds;
            var min = bounds.transform.TransformPoint(b.min);
            var max = bounds.transform.TransformPoint(b.max);
            var halfH = m_Camera.orthographicSize;
            var halfW = halfH * m_Camera.aspect;
            result.x = max.x - min.x <= 2 * halfW ? (min.x + max.x) / 2 : Mathf.Clamp(result.x, min.x + halfW, max.x - halfW);
            result.y = max.y - min.y <= 2 * halfH ? (min.y + max.y) / 2 : Mathf.Clamp(result.y, min.y + halfH, max.y - halfH);
            return result;
        }
    }
}
