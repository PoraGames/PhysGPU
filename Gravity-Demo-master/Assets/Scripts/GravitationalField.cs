﻿
namespace GravityDemo
{
    using UnityEngine;

    [ExecuteInEditMode]
    public sealed class GravitationalField : GravitationalObject
    {
        #region CONSTANTS
        private const int DefaultDimensions = 8;
        #endregion

        #region FIELDS
        [SerializeField, HideInInspector]
        private GravitationalBodyManager bodies;

        [SerializeField, HideInInspector] private int width  = DefaultDimensions;
        [SerializeField, HideInInspector] private int height = DefaultDimensions;
        [SerializeField, HideInInspector] private int depth  = DefaultDimensions;
        [SerializeField, HideInInspector] private int margin = DefaultDimensions;

        #if UNITY_EDITOR
        [SerializeField] private int _width  = DefaultDimensions;
        [SerializeField] private int _height = DefaultDimensions;
        [SerializeField] private int _depth  = DefaultDimensions;
        [SerializeField] private int _margin = DefaultDimensions;
        #endif

        [SerializeField, HideInInspector] private ComputeShader gravitationalField;
        [SerializeField, HideInInspector] private ComputeShader gravitationalFieldVelocity;
        [SerializeField, HideInInspector] private Material      pointsMaterial;
        [SerializeField, HideInInspector] private Material      gridMaterial;

        [SerializeField] private bool drawPoints = false;
        [SerializeField] private bool drawGrid   = true;

        private ComputeBuffer pointBuffer;
        private ComputeBuffer gridBuffer;

        private int computePointPositionsKernel;
        private int computeDisplacementKernel;
        private int computeGridKernel;
        private int computeVelocityKernel;
        #endregion

        #region PROPERTIES
        public int Width  { get { return width;  } set { width  = Mathf.Max(1, value); } }
        public int Height { get { return height; } set { height = Mathf.Max(1, value); } }
        public int Depth  { get { return depth;  } set { depth  = Mathf.Max(1, value); } }
        public int Margin { get { return margin; } set { margin = Mathf.Max(0, value); } }

        private int W          { get { return width  + 1; } }
        private int H          { get { return height + 1; } }
        private int D          { get { return depth  + 1; } }
        private int ThreadsX   { get { return W;          } }
        private int ThreadsY   { get { return H;          } }
        private int ThreadsZ   { get { return D;          } }
        private int PointCount { get { return W * H * D;  } }
        #endregion

        #region AWAKE
        private void Awake()
        {
            #if UNITY_EDITOR
            OnValidate();
            #endif
        }
        #endregion

        #region ON VALIDATE
        private void OnValidate()
        {
            #if UNITY_EDITOR
            Width  = _width;  _width  = Width;
            Height = _height; _height = Height;
            Depth  = _depth;  _depth  = Depth;
            Margin = _margin; _margin = Margin;
            #endif
        }
        #endregion

        #region ON ENABLE / DISABLE
        private void OnEnable()
        {
            if (bodies == null)
            {
                bodies =
                new GameObject().AddComponent<GravitationalBodyManager>();
                bodies.transform.parent = transform;
            }

            LoadResource("GravitationalField",         ref gravitationalField);
            LoadResource("GravitationalFieldVelocity", ref gravitationalFieldVelocity);
            LoadResource("GravitationalFieldPoints",   ref pointsMaterial);
            LoadResource("GravitationalFieldGrid",     ref gridMaterial);

            computePointPositionsKernel = gravitationalField.FindKernel("ComputePointPositions");
            computeDisplacementKernel   = gravitationalField.FindKernel("ComputeDisplacement");
            computeGridKernel           = gravitationalField.FindKernel("ComputeGrid");

            computeVelocityKernel = gravitationalFieldVelocity.FindKernel("ComputeVelocity");
        }

        private void OnDisable()
        {
            ReleaseComputeBuffer(ref pointBuffer);
            ReleaseComputeBuffer(ref gridBuffer);
        }
        #endregion

        #region ON DESTROY
        private void OnDestroy()
        {
            Resources.UnloadAsset(pointsMaterial);
            Resources.UnloadAsset(gridMaterial);
        }
        #endregion

        #region ON RENDER OBJECT
        private void OnRenderObject()
        {
            ValidatePointBuffer();
            ValidateGridBuffer();

            if (bodies.Count > 0)
                gravitationalField.SetBuffer(computeDisplacementKernel, "body_buffer", bodies.Buffer);
            gravitationalField.SetInt("body_count", bodies.Count);
            gravitationalField.SetBuffer(computeDisplacementKernel, "point_buffer", pointBuffer);
            gravitationalField.Dispatch(computeDisplacementKernel, ThreadsX, ThreadsY, ThreadsZ);

            if (drawPoints)
                DrawField(pointsMaterial);

            if (drawGrid)
            {
                gravitationalField.Dispatch(computeGridKernel, ThreadsX, ThreadsY, ThreadsZ);
                DrawField(gridMaterial);
            }

            ComputeVelocity();
        }
        #endregion

        #region METHODS
        private void ValidatePointBuffer()
        {
            if (ValidateComputeBuffer(PointCount, sizeof(float) * 3 * 2, ref pointBuffer))
            {
                gravitationalField.SetInt   ("w", W);
                gravitationalField.SetInt   ("h", H);
                gravitationalField.SetInt   ("d", D);
                gravitationalField.SetVector("offset", new Vector3(width, height, depth) * 0.5f);
                gravitationalField.SetBuffer(computePointPositionsKernel, "point_buffer", pointBuffer);
                gravitationalField.Dispatch(computePointPositionsKernel, ThreadsX, ThreadsY, ThreadsZ);

                pointsMaterial.SetBuffer("point_buffer", pointBuffer);
            }
        }

        private void ValidateGridBuffer()
        {
            if (ValidateComputeBuffer(PointCount, sizeof(uint) * 3, ref gridBuffer))
            {
                gravitationalField.SetBuffer(computeGridKernel, "point_buffer", pointBuffer);
                gravitationalField.SetBuffer(computeGridKernel, "grid_buffer",  gridBuffer);

                gridMaterial.SetBuffer("point_buffer", pointBuffer);
                gridMaterial.SetBuffer("grid_buffer",  gridBuffer);
            }
        }

        private void DrawField(Material material)
        {
            material.SetPass(0);
            material.SetMatrix("object_to_world", transform.localToWorldMatrix);
            Graphics.DrawProcedural(MeshTopology.Points, PointCount);
        }

        private void ComputeVelocity()
        {
            if (Application.isPlaying)
            {
                if (bodies.Count > 0)
                {
                    gravitationalFieldVelocity.SetInt   (                       "w",            W);
                    gravitationalFieldVelocity.SetInt   (                       "h",            H);
                    gravitationalFieldVelocity.SetInt   (                       "d",            D);
                    gravitationalFieldVelocity.SetInt   (                       "margin",       margin);
                    gravitationalFieldVelocity.SetFloat (                       "delta_time",   Time.deltaTime);
                    gravitationalFieldVelocity.SetBuffer(computeVelocityKernel, "point_buffer", pointBuffer);
                    gravitationalFieldVelocity.SetBuffer(computeVelocityKernel, "body_buffer",  bodies.Buffer);
                    gravitationalFieldVelocity.Dispatch(computeVelocityKernel, bodies.Count, 1, 1);
                }
            }
        }
        #endregion

        #region ON DRAW GIZMOS
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1, 1, 1, 0.25f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, depth));

            int w_margin = margin + width  + margin;
            int h_margin = margin + height + margin;
            int d_margin = margin + depth  + margin;
            Gizmos.color = new Color(1, 1, 1, 0.15f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(w_margin, h_margin, d_margin));
        }
        #endregion
    }
}