﻿using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.Rendering;

using Portals.MonoBehaviours;

using System;

using Il2Cpp;

namespace Portals.Rendering;

public class PortalCamera
{
    public Camera Camera { get; set; }

    public Transform Transform { get; set; }

    public GameObject GameObject { get; set; }

    private RenderTexture _targetTexture = null;
    public RenderTexture TargetTexture
    {
        get
        {
            return _targetTexture;
        }
        set
        {
            _targetTexture = value;

            Camera.targetTexture = value;

            OnTargetTextureChanged?.Invoke(value);
        }
    }

    public Camera.StereoscopicEye Eye { get; set; }

    public VolumetricRendering VolumetricRendering { get; set; }

    public event Action<RenderTexture> OnTargetTextureChanged;

    public PortalCamera(Portal portal, Camera.StereoscopicEye eye)
    {
        GameObject = new GameObject($"{eye} Portal Camera");
        GameObject.SetActive(false);

        Transform = GameObject.transform;

        Transform.parent = portal.transform;

        Camera = GameObject.AddComponent<Camera>();
        Camera.enabled = false;
        Camera.useOcclusionCulling = false; // Breaks with oblique camera matrices
        Camera.allowHDR = true;
        Camera.allowMSAA = false;
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = Color.black;
        Camera.stereoTargetEye = StereoTargetEyeMask.None;

        var data = GameObject.AddComponent<UniversalAdditionalCameraData>();
        data.antialiasing = AntialiasingMode.None;
        data.allowXRRendering = false; // Having this enabled causes unnecessary effects like VRS to occur, and seemingly causes access violations as well!

        VolumetricRendering = VolumetricCreator.AddVolumetricRendering(Camera);

        GameObject.SetActive(true);

        var (width, height) = GetDimensions();

        Eye = eye;

        PortalPreferences.OnRenderScaleChanged += OnRenderScaleChanged;
        PortalPreferences.OnRenderVolumetricsChanged += OnRenderVolumetricsChanged;

        OnRenderScaleChanged(PortalPreferences.RenderScale);
        OnRenderVolumetricsChanged(PortalPreferences.RenderVolumetrics);
    }

    public void ReleaseTexture()
    {
        if (TargetTexture != null)
        {
            TargetTexture.Release();
            TargetTexture = null;
        }
    }

    private void OnRenderScaleChanged(float value)
    {
        ReleaseTexture();

        var (width, height) = GetDimensions();

        width = Mathf.RoundToInt(width * value);
        height = Mathf.RoundToInt(height * value);

        // Make sure to keep the depth at 24, depth of 0 doesn't render properly on Quest
        TargetTexture = new RenderTexture(width, height, 24);
    }

    private void OnRenderVolumetricsChanged(bool value)
    {
        VolumetricRendering.enabled = value;
    }

    private static (int width, int height) GetDimensions()
    {
        int width = XRSettings.eyeTextureWidth;
        int height = XRSettings.eyeTextureHeight;

        if (width <= 0 || height <= 0)
        {
            width = Screen.width;
            height = Screen.height;
        }

        return (width, height);
    }

    public void Destroy()
    {
        PortalPreferences.OnRenderScaleChanged -= OnRenderScaleChanged;
        PortalPreferences.OnRenderVolumetricsChanged -= OnRenderVolumetricsChanged;

        GameObject.Destroy(GameObject);

        ReleaseTexture();
    }

    public void RenderCamera(ScriptableRenderContext context)
    {
        UpdateVolumetrics();

        UniversalRenderPipeline.RenderSingleCamera(context, Camera);
    }

    private void UpdateVolumetrics()
    {
        if (!PortalPreferences.RenderVolumetrics)
        {
            return;
        }

        Camera.enabled = true;

        try
        {
            VolumetricRendering.UpdateFunc();
        }
        catch (Exception e)
        {
            PortalsMod.Logger.Error("Failed invoking VolumetricRendering.UpdateFunc!", e);
        }

        Camera.enabled = false;
    }
}
