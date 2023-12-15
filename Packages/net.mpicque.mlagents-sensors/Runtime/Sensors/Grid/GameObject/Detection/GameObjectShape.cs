using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

namespace Sensors.Grid
{
    /// <summary>
    ///     Stores shape points for a <see cref="DetectableGameObject" />
    ///     and manages levels of detail.
    /// </summary>
    [Serializable]
    public class GameObjectShape
    {
        [SerializeField]
        [EnableIf("IsNotPlaying")]
        [OnValueChanged(nameof(OnMergeChange))]
        [AllowNesting]
        [Tooltip("Whether to merge disconnected colliders into a " +
                 "single volume when scanning and calculating LODs.")]
        private bool m_MergeDisconnected;

        [SerializeField]
        [EnableIf("IsNotPlaying")]
        [OnValueChanged(nameof(OnFlattenChange))]
        [AllowNesting]
        [Tooltip("Flatten shape points for 2D detection." +
                 "\n2D LOD is fixed at Scan LOD value.")]
        private bool m_Flatten;

        [SerializeField]
        [EnableIf(EConditionOperator.And, "IsNotPlaying", "Is3D")]
        [OnValueChanged(nameof(OnProjectionChange))]
        [AllowNesting]
        [Tooltip("Project shape points onto collider walls." +
                 "\nWorks best with single convex colliders.")]
        [Range(0, 1)]
        private float m_Projection;

        [SerializeField] [ReadOnly] [AllowNesting] [Tooltip("Number of detectable points\nat selected Gizmo LOD.")]
        private int m_PointCount;

        [SerializeField]
        [Tooltip("Edit to show different grid resolutions."
                 + "\nThis is meant for testing purposes and doesn't reflect the"
                 + " grid settings applied to the sensor.\nGizmo grid cells are drawn"
                 + " in world space.\n0: Grid draw off. Disabled at runtime.")]
        [Min(0)]
        private float m_DrawGrid;


        /// <summary>
        ///     Whether to merge disconnected colliders into a
        ///     single volume when scanning and calculating LODs.
        /// </summary>
        public bool Merge => m_MergeDisconnected;


        /// <summary>
        ///     Whether to flatten the object's shape for 2D detection.
        ///     Points will be projected onto the world's XZ-plane.
        /// </summary>
        public bool Flatten => m_Flatten;


        /// <summary>
        ///     The amount by which points are being projected onto the
        ///     collider walls. Works best with single convex colliders.
        /// </summary>
        public float Projection => m_Projection;


        // Inspector flags for NaughtyAttributes.
        private bool IsNotPlaying => !Application.isPlaying;
        private bool Is3D => !m_Flatten;

        /// <summary>
        ///     Invoked on inspector changes.
        /// </summary>
        public event Action RequireScanEvent;

        private void OnMergeChange()
        {
            RequireScanEvent.Invoke();
        }

        private void OnFlattenChange()
        {
            m_Projection = 0;
            m_GizmoLOD = m_ScanLOD;
            m_SelectedLOD = m_ScanLOD;
            RequireScanEvent.Invoke();
        }

        private void OnProjectionChange()
        {
            // TODO Shouldn't need to invoke new scan for changing projection.
            RequireScanEvent.Invoke();
        }


        /// <summary>
        ///     Resets the <see cref="GameObjectShape" />.
        /// </summary>
        public void Reset()
        {
            m_MaxLOD = 0;
            m_ScanLOD = 0;
            m_GizmoLOD = 0;
            m_SelectedLOD = 0;

            m_DrawGrid = 0;
            m_PointCount = 0;
            m_Projection = 0;

            Clear();
        }

        /// <summary>
        ///     Handles the <see cref="ShapeScanUtil" /> result.
        /// </summary>
        /// <param name="maxLOD">Highest LOD in the scan result</param>
        /// <param name="result">List of <see cref="ScanResultLOD" /> instances</param>
        public void OnScanResult(int maxLOD, List<ScanResultLOD> result)
        {
            Clear();

            m_ScanResultsByLOD = result;
            m_MaxLOD = maxLOD;
            m_ScanLOD = Mathf.Clamp(m_ScanLOD, 0, maxLOD);
            m_GizmoLOD = Mathf.Clamp(m_GizmoLOD, 0, maxLOD);
            SetSelectedLOD(m_SelectedLOD);
        }

        /// <summary>
        ///     Returns a list of world space points for a specific LOD,
        ///     depending on the distance between sensor and gameobject.
        /// </summary>
        /// <param name="transform"><see cref="DetectableGameObject" /> transform</param>
        /// <param name="normDistance">Normalized distance</param>
        /// <returns>List of points in world space</returns>
        public IList<Vector3> GetWorldPointsAtDistance(Transform transform, float normDistance)
        {
            // normDistance 0 (near) to 1 (far), invert -> max LOD when closest.
            SetSelectedLOD(Mathf.RoundToInt((1 - normDistance) * m_MaxLOD));

            return GetWorldPoints(transform);
        }

        private IList<Vector3> GetWorldPoints(Transform transform)
        {
            var matrix = transform.localToWorldMatrix;
            m_WorldPoints.Clear();

            foreach (var point in m_LocalPoints)
            {
                m_WorldPoints.Add(matrix.MultiplyPoint3x4(point));
            }

            return m_WorldPoints;
        }

        private void SetSelectedLOD(int LOD)
        {
            m_SelectedLOD = m_Flatten ? 0 : Mathf.Clamp(LOD, 0, m_MaxLOD);
            m_LocalPoints = m_ScanResultsByLOD[m_SelectedLOD].LocalPoints;
            m_PointCount = m_LocalPoints.Count; // Info

            if (Application.isPlaying)
            {
                // Keep the disabled Gizmo LOD slider synced with the 
                // current LOD requested by the detector at runtime.
                m_GizmoLOD = m_SelectedLOD;
            }
        }


        /// <summary>
        ///     Draws all shape points and highlights points for the selected LOD.
        ///     Draws optional debug grid around highlighted points.
        /// </summary>
        /// <param name="transform"><see cref="DetectableGameObject" /> transform</param>
        public void DrawGizmos(Transform transform)
        {
            if (HasPoints())
            {
                var matrix = transform.localToWorldMatrix;

                for (int i = 0, n = m_ScanResultsByLOD.Count; i < n; i++)
                {
                    var isSelectedLOD = i == m_SelectedLOD;
                    var size = Vector3.one * (isSelectedLOD ? 0.05f : 0.025f);
                    Gizmos.color = isSelectedLOD ? Color.red : Color.grey;

                    var localPoints = m_ScanResultsByLOD[i].LocalPoints;
                    foreach (var point in localPoints)
                    {
                        var p = matrix.MultiplyPoint3x4(point);
                        Gizmos.DrawCube(p, size);
                    }

                    if (isSelectedLOD)
                    {
                        m_PointCount = localPoints.Count; // Info
                    }
                }

                if (m_DrawGrid >= 0.1f)
                {
                    var scale = Vector3.one * m_DrawGrid;

                    var colWire = Color.blue * 0.75f;
                    var colFill = Color.blue * 0.4f;

                    var worldPoints = GetWorldPoints(transform);
                    var filterDuplicates = new HashSet<Vector3>();

                    foreach (var point in worldPoints)
                    {
                        var rounded = new Vector3(
                            Mathf.RoundToInt(point.x / m_DrawGrid),
                            Mathf.RoundToInt(point.y / m_DrawGrid),
                            Mathf.RoundToInt(point.z / m_DrawGrid)) * m_DrawGrid;

                        if (filterDuplicates.Add(rounded))
                        {
                            Gizmos.color = colWire;
                            Gizmos.DrawWireCube(rounded, scale);
                            Gizmos.color = colFill;
                            Gizmos.DrawCube(rounded, scale);
                        }
                    }
                }
            }
        }


        #region Point Collections

        // Created by ShapeScanUtil.
        [SerializeField] [HideInInspector] private List<ScanResultLOD> m_ScanResultsByLOD;

        // References ScanResultLOD[LOD].LocalPoints
        [SerializeField] [HideInInspector] private List<Vector3> m_LocalPoints;

        // On demand: m_LocalPoints -> world.
        [SerializeField] [HideInInspector] private List<Vector3> m_WorldPoints = new();

        /// <summary>
        ///     Whether there are any points stored.
        /// </summary>
        /// <returns></returns>
        private bool HasPoints() => m_ScanResultsByLOD != null && m_ScanResultsByLOD.Count > 0;

        /// <summary>
        ///     Removes all shape points.
        /// </summary>
        private void Clear()
        {
            if (HasPoints())
            {
                m_ScanResultsByLOD.Clear();
                m_WorldPoints.Clear();
                m_LocalPoints.Clear();
            }
        }

        #endregion


        #region LOD

        // The highest available LOD given the current Scan LOD setting.
        [SerializeField] [HideInInspector] private int m_MaxLOD;

        // The LOD used for selecting a point list from the scan result.
        [SerializeField] [HideInInspector] private int m_SelectedLOD;


        /// <summary>
        ///     The LOD applied to the <see cref="ShapeScanUtil" />.
        /// </summary>
        public int ScanLOD => m_ScanLOD;

        [SerializeField]
        [EnableIf("IsNotPlaying")]
        [OnValueChanged(nameof(OnScanLODChange))]
        [AllowNesting]
        [Tooltip("Scan level of detail.")]
        [Range(0, 7)]
        private int m_ScanLOD;

        private void OnScanLODChange()
        {
            // Gizmo LOD follows Scan LOD inspector value.
            m_GizmoLOD = m_ScanLOD;
            RequireScanEvent.Invoke();

            OnGizmoLODChange();
        }

        // The LOD used for highlighting Gizmo points.
        [SerializeField]
        [EnableIf(EConditionOperator.And, "IsNotPlaying", "Is3D")]
        [OnValueChanged(nameof(OnGizmoLODChange))]
        [AllowNesting]
        [Tooltip("Gizmo level of detail <= Scan LOD.")]
        [Range(0, 7)]
        private int m_GizmoLOD;

        private void OnGizmoLODChange()
        {
            // Gizmo LOD can't be higher than Scan LOD.
            m_GizmoLOD = Mathf.Clamp(m_GizmoLOD, 0, m_ScanLOD);
            // Need to select matching point list after 
            // inspector settings change.
            SetSelectedLOD(m_GizmoLOD);
        }

        #endregion
    }
}