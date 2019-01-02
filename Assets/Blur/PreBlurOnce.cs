using System;
using System.Collections.Generic;
//using SLua;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

//[CustomLuaClass]
public class PreBlurOnce : MonoBehaviour
{
    public int downsample = 1;
    public float blurSize = 3.0f;

    protected List<Camera> disabledCameras = new List<Camera>();

    private Material blurMaterial;
    private RenderTexture cacheTexture;
    private PreBlurOnce nextBlur;
    private PreBlurOnce previousBlur;
    private bool took;
    private RenderTexture rt;

    private static int WaterMask;
    private Camera selfCam;

    void Awake()
    {
        if (WaterMask <= 0)
        {
            WaterMask = 1 << LayerMask.NameToLayer("Water");
        }
        selfCam = GetComponent<Camera>();

        RenderPipeline.beginCameraRendering += RenderPipeline_BeginCameraRendering;
    }

    void RenderPipeline_BeginCameraRendering(Camera obj)
    {
        if (obj == selfCam)
        {
            OnPreRender();
        }
    }


    protected void OnEnable()
    {
        ClosePreviousBlur();
    }

    protected void OnDisable()
    {
        ReleaseRT();
    }

    protected void OnDestroy()
    {
        if (blurMaterial != null)
        {
            Destroy(blurMaterial);
            blurMaterial = null;
        }

        ReleaseCacheRT();

        if (nextBlur != null)
        {
            disabledCameras.Reverse();
            foreach (Camera disabledCamera in disabledCameras)
            {
                if (disabledCamera != null && !nextBlur.disabledCameras.Contains(disabledCamera))
                {
                    nextBlur.disabledCameras.Insert(0, disabledCamera);
                }
            }

            nextBlur.SetPreviousBlur(previousBlur);
        }
        else
        {
            EnableCameras();

            if (previousBlur != null)
            {
                previousBlur.SetNextBlur(null);
                previousBlur.enabled = true;
            }
        }

        RenderPipeline.beginCameraRendering -= RenderPipeline_BeginCameraRendering;
    }

    private void OnPreRender()
    {
        //Debug.Log("OnPreRender");
        if (rt == null || !rt.IsCreated())
        {
            // The rt is lost. Take again.
            took = false;
        }

        BuildDisabledCamera();

        if (!took)
        {
            took = true;

            blurSize = Screen.width >> 8;
            blurSize *= 0.6f;
            blurSize = Math.Max(1.9f, Math.Min(blurSize, 6));

            if (previousBlur == null)
            {
                PrepareCacheTexture();
                RenderBlur(cacheTexture);
            }
            else
            {
                RenderTexture tempRt = RenderTexture.GetTemporary(Screen.width >> downsample, Screen.height >> downsample, 16);
                tempRt.filterMode = FilterMode.Bilinear;
                if (!tempRt.IsCreated())
                {
                    tempRt.Create();
                }

                tempRt.DiscardContents();

                // Clear the depth
                RenderTexture previousRt = RenderTexture.active;
                RenderTexture.active = tempRt;
                GL.Clear(true, false, Color.black, 0);
                RenderTexture.active = previousRt;

                RenderRaw(tempRt);
                RenderBlur(tempRt);
                RenderTexture.ReleaseTemporary(tempRt);
            }
        }

        //disabledCameras[0].targetTexture = rt;
        Graphics.Blit(rt, null as RenderTexture, blurMaterial, 0);
    }

    private void BuildDisabledCamera()
    {
        if (disabledCameras.Count == 0)
        {
            Camera[] cameras = Camera.allCameras;
            Array.Sort(cameras, CompareCamera);
            foreach (Camera cameraItem in cameras)
            {
                if (cameraItem == GetComponent<Camera>())
                {
                    break;
                }

                // Disable camera to improve performance
                Debug.Log("BuildDisabledCamera");
                cameraItem.enabled = false;
                disabledCameras.Add(cameraItem);
                took = false; // Camera changed. Take again.
            }
        }
    }

    private void RenderRaw(RenderTexture targetRT)
    {
        if (previousBlur != null)
        {
            previousBlur.RenderRaw(targetRT);
            RenderDisabledCameras(targetRT);
        }
        else
        {
            PrepareCacheTexture();
            Graphics.Blit(cacheTexture, targetRT);
        }
    }

    private void PrepareCacheTexture()
    {
        bool render = false;
        if (cacheTexture == null)
        {
            cacheTexture = RenderTexture.GetTemporary(Screen.width >> downsample, Screen.height >> downsample, 16);
            cacheTexture.filterMode = FilterMode.Bilinear;
            render = true;
        }

        if (!cacheTexture.IsCreated())
        {
            cacheTexture.Create();
            render = true;
        }

        if (render)
        {
            Debug.Log("PrepareCacheTexture");
            cacheTexture.DiscardContents();
            RenderDisabledCameras(cacheTexture);
        }
    }

    private void RenderDisabledCameras(RenderTexture targetRT)
    {
        BuildDisabledCamera();

        foreach (Camera cameraItem in disabledCameras)
        {
            if (cameraItem != null)
            {
                RenderTexture previousRt = cameraItem.targetTexture;
                // The "Render to texture" camera does not need to render
                if (previousRt == null)
                {
                    Debug.Log("previousRt is null");
                    int cullingMask = cameraItem.cullingMask;
                    if ((cullingMask & WaterMask) == WaterMask)
                    {
                        // Cull water to avoid rendering wired effect.
                        cameraItem.cullingMask = cullingMask ^ WaterMask;
                    }

                    cameraItem.targetTexture = targetRT;
                    cameraItem.Render();
                    cameraItem.targetTexture = previousRt;

                    cameraItem.cullingMask = cullingMask;
                }
            }
        }
    }

    private void ClosePreviousBlur()
    {
        if (previousBlur == null)
        {
            Camera[] cameras = Camera.allCameras;
            Array.Sort(cameras, CompareCamera);

            PreBlurOnce blur = null;
            foreach (Camera cameraItem in cameras)
            {
                if (cameraItem == GetComponent<Camera>())
                {
                    break;
                }

                var currentBlur = cameraItem.GetComponent<PreBlurOnce>();
                if (currentBlur != null)
                {
                    blur = currentBlur;
                }
            }

            if (blur != null)
            {
                SetPreviousBlur(blur);
            }
        }

        if (previousBlur != null)
        {
            previousBlur.enabled = false;
        }
    }

    private void SetPreviousBlur(PreBlurOnce blur)
    {
        previousBlur = blur;
        if (previousBlur != null)
        {
            previousBlur.nextBlur = this;
        }
    }

    private void SetNextBlur(PreBlurOnce blur)
    {
        nextBlur = blur;
        if (nextBlur != null)
        {
            nextBlur.previousBlur = this;
        }
    }

    private int CompareCamera(Camera c0, Camera c1)
    {
        float v = c0.depth - c1.depth;
        if (v < 0)
        {
            return -1;
        }

        if (v > 0)
        {
            return 1;
        }

        return 0;
    }

    private void RenderBlur(RenderTexture source)
    {
        if (blurMaterial == null)
        {
            //Shader blurShader = ResManager.GetShader("Custom/BlurOnce");
            Shader blurShader = Shader.Find("Custom/BlurOnce");
            blurMaterial = new Material(blurShader);
            blurMaterial.hideFlags = HideFlags.DontSave;
            blurMaterial.SetFloat("_Light", 0.6f);
        }

        int rtW = source.width;
        int rtH = source.height;
        float widthMod = 1.0f / (1 << downsample);

        if (rt == null)
        {
            rt = RenderTexture.GetTemporary(rtW, rtH, 0, source.format);
            rt.filterMode = FilterMode.Bilinear;
        }
        
        if (!rt.IsCreated())
        {
            rt.Create();
        }

        RenderTexture rt2 = RenderTexture.GetTemporary(rtW, rtH, 0, source.format);
        rt2.filterMode = FilterMode.Bilinear;
        if (!rt2.IsCreated())
        {
            rt2.Create();
        }

        blurMaterial.SetFloat("_Parameter", blurSize * widthMod + 0.4f);
        rt.DiscardContents();
        Graphics.Blit(source, rt, blurMaterial, 1);

        for (int i = 0; i < 2; i++)
        {
            float iterationOffs = (i * 1.0f + 0.8f);
            blurMaterial.SetFloat("_Parameter", blurSize * widthMod + iterationOffs);

            // vertical blur
            rt2.DiscardContents();
            Graphics.Blit(rt, rt2, blurMaterial, 2);
            
            // horizontal blur
            rt.DiscardContents();
            Graphics.Blit(rt2, rt, blurMaterial, 1);
        }

        RenderTexture.ReleaseTemporary(rt2);
    }

    private void EnableCameras()
    {
        if (disabledCameras.Count > 0)
        {
            foreach (Camera cameraItem in disabledCameras)
            {
                if (cameraItem != null)
                {
                    cameraItem.enabled = true;
                }
            }

            disabledCameras.Clear();
        }
    }

    private void ReleaseRT()
    {
        if (rt != null)
        {
            RenderTexture.ReleaseTemporary(rt);
            rt = null;
        }
    }

    private void ReleaseCacheRT()
    {
        if (cacheTexture != null)
        {
            RenderTexture.ReleaseTemporary(cacheTexture);
            cacheTexture = null;
        }
    }
}