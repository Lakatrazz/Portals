﻿using System;
using System.Collections.Generic;

using MelonLoader;

using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Utilities;
using Il2CppSLZ.Marrow.Warehouse;
using Il2CppSLZ.VRMK;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow.Interaction;

using Il2CppInterop.Runtime.Attributes;

using UnityEngine;

using Portals.Rendering;

namespace Portals.MonoBehaviours;

[RegisterTypeInIl2Cpp]
public class TeleportableRigManager : Teleportable
{
    public struct PendingTransform
    {
        public Transform transform;
        public Vector3 position;
        public Quaternion rotation;
    }

    public TeleportableRigManager(IntPtr intPtr) : base(intPtr) { }

    public delegate bool ScaleCallback(TeleportableRigManager teleportable, float scale);

    public static event ScaleCallback OnScaleEvent;

    public RigManager RigManager => _rigManager;

    private RigManager _rigManager = null;

    public PhysicsRig PhysicsRig => _physicsRig;
    private PhysicsRig _physicsRig = null;

    public OpenControllerRig OpenControllerRig => _openControllerRig;
    private OpenControllerRig _openControllerRig = null;

    public Transform Headset => _headset;
    private Transform _headset = null;

    [HideFromIl2Cpp]
    public InventorySlotReceiver[] Slots => _slots;
    private InventorySlotReceiver[] _slots = null;

    public WindBuffetSFX WindBuffetSFX => _windBuffetSfx;
    private WindBuffetSFX _windBuffetSfx = null;

    protected override void OnTeleportableAwake()
    {
        base.OnTeleportableAwake();

        _rigManager = GetComponent<RigManager>();

        _rigManager.onAvatarSwapped += (Il2CppSystem.Action)OnAvatarSwapped;

        _openControllerRig = _rigManager.controllerRig.TryCast<OpenControllerRig>();
        _headset = _openControllerRig.headset;

        _windBuffetSfx = _headset.GetComponentInChildren<WindBuffetSFX>();

        _slots = GetComponentsInChildren<InventorySlotReceiver>();

        _physicsRig = _rigManager.physicsRig;

        var marrowEntity = PhysicsRig.marrowEntity;

        SetupEntity(marrowEntity);
    }

    protected override void OnTeleportableDestroy()
    {
        base.OnTeleportableDestroy();

        UnhookRig();
    }

    protected override void OnEnsureClone()
    {
        HookRig();

        CreateClone(RigManager.gameObject);
    }

    private bool _hooked = false;
    private Il2CppSystem.Action _onPostLateUpdate = null;

    private void HookRig()
    {
        if (_hooked)
        {
            return;
        }

        _onPostLateUpdate = (Il2CppSystem.Action)OnPostLateUpdate;
        _rigManager.OnPostLateUpdate += _onPostLateUpdate;
        _hooked = true;
    }

    private void UnhookRig()
    {
        if (!_hooked)
        {
            return;
        }

        _rigManager.OnPostLateUpdate -= _onPostLateUpdate;
        _onPostLateUpdate = null;

        _hooked = false;
    }

    protected override void SetupBody(MarrowBody marrowBody)
    {
        // Don't track the BodyLog
        if (marrowBody.name.ToLower() == "spheregrip")
        {
            return;
        }

        base.SetupBody(marrowBody);
    }

    protected override void OnPortalsChanged(Portal inPortal, Portal outPortal)
    {
        base.OnPortalsChanged(inPortal, outPortal);

        TransferHands(PhysicsRig.leftHand, PhysicsRig.rightHand, inPortal);
    }

    private void OnPostLateUpdate()
    {
        UpdateRotationCorrection();

        if (!HasPortals)
        {
            return;
        }

        UpdateClone();

        var inPortal = _inPortal;
        var outPortal  = _outPortal;

        var newSign = GetPortalSign(inPortal);

        if (PassedThrough(_initialSign, newSign) && InBounds(_inPortal, GetAnchor()) && !IsGrabbed())
        {
            TryTeleport(inPortal, outPortal);
        }
    }

    private bool _correctRotation = false;
    private bool _correctBallLoco = false;

    private void UpdateRotationCorrection()
    {
        if (!_correctRotation)
        {
            return;
        }

        if (RigManager.bodyState != RigManager.BodyState.OnFoot)
        {
            return;
        }

        var remapHeptaRig = RigManager.remapHeptaRig;
        var currentRotation = remapHeptaRig.transform.rotation;
        var targetRotation = Quaternion.FromToRotation(currentRotation * Vector3.up, Vector3.up) * currentRotation;

        var slerpRotation = Quaternion.Slerp(currentRotation, targetRotation, 1f - Mathf.Exp(-8f * Time.deltaTime));

        if (Quaternion.Angle(targetRotation, slerpRotation) <= 1f)
        {
            slerpRotation = targetRotation;
            _correctRotation = false;

            if (_correctBallLoco)
            {
                ToggleBallLoco(true);
                _correctBallLoco = false;
            }
        }

        remapHeptaRig.transform.rotation = slerpRotation;
    }

    private void ToggleBallLoco(bool enabled)
    {
        if (enabled)
        {
            PhysicsRig.EnableBallLoco();

            var pelvisPosition = PhysicsRig.m_pelvis.position;
            PhysicsRig.feet.transform.position = pelvisPosition;
            PhysicsRig.knee.transform.position = pelvisPosition;
        }
        else
        {
            PhysicsRig.DisableBallLoco();
        }
    }

    private void OnAvatarSwapped()
    {
        CalculateTrackers();

        if (HasClone)
        {
            CreateClone(RigManager.gameObject);
        }
    }

    public override Vector3 GetAnchor()
    {
        return Headset.position;
    }

    protected override void OnTeleport(Portal inPortal, Portal outPortal)
    {
        var inPortalMatrix = inPortal.PortalEnterMatrix;
        var outPortalMatrix = outPortal.PortalExitMatrix;

        var pendingTransforms = new List<PendingTransform>();
        foreach (var rigidbody in MarrowEntity.Bodies)
        {
            var rigidbodyTransform = rigidbody.transform;
            var rigidbodyMatrix = CalculateTeleportedMatrix(rigidbodyTransform.localToWorldMatrix, inPortalMatrix, outPortalMatrix);

            pendingTransforms.Add(new PendingTransform()
            {
                transform = rigidbody.transform,
                position = rigidbodyMatrix.GetPosition(),
                rotation = rigidbodyMatrix.GetRotation(),
            });
        }

        // Rotating the rigs while in a seat can cause issues
        bool seated = RigManager.activeSeat;

        if (!seated)
        {
            foreach (var rig in RigManager.remapRigs)
            {
                pendingTransforms.Add(CreatePendingTransform(rig.transform, inPortalMatrix, outPortalMatrix));
            }

            pendingTransforms.Add(CreatePendingTransform(OpenControllerRig.transform, inPortalMatrix, outPortalMatrix));
            pendingTransforms.Add(CreatePendingTransform(OpenControllerRig.vrRoot, inPortalMatrix, outPortalMatrix));
        }

        var anchor = PhysicsRig.centerOfPressure;

        var newMatrix = CalculateTeleportedMatrix(anchor.localToWorldMatrix, inPortalMatrix, outPortalMatrix);

        var newPosition = newMatrix.GetPosition();
        var newRotation = newMatrix.rotation;

        var displacePosition = newPosition - anchor.position;
        var displaceRotation = newRotation * Quaternion.Inverse(anchor.rotation);

        var displaceTransform = SimpleTransform.Create(displacePosition, displaceRotation);

        OpenControllerRig.Teleport(displaceTransform, false);

        foreach (var rig in RigManager.remapRigs)
        {
            rig.Teleport(displaceTransform, false);
        }

        PhysicsRig.Teleport(displaceTransform, false);

        foreach (var pendingTransform in pendingTransforms)
        {
            pendingTransform.transform.position = pendingTransform.position;
            pendingTransform.transform.rotation = pendingTransform.rotation;
        }

        foreach (var body in MarrowEntity.Bodies)
        {
            if (!body.HasRigidbody)
            {
                continue;
            }

            var rigidbody = body._rigidbody;

            rigidbody.velocity = outPortalMatrix.MultiplyVector(inPortalMatrix.inverse.MultiplyVector(rigidbody.velocity - inPortal.Velocity)) + outPortal.Velocity;
            rigidbody.angularVelocity = outPortalMatrix.rotation * (inPortalMatrix.inverse.rotation * (rigidbody.angularVelocity - inPortal.AngularVelocity)) + outPortal.AngularVelocity;
        }

        var remapRig = RigManager.remapHeptaRig;

        remapRig._currentVelocity = TransformVector2(remapRig._currentVelocity, inPortalMatrix, outPortalMatrix);
        remapRig._currentAcceleration = TransformVector2(remapRig._currentAcceleration, inPortalMatrix, outPortalMatrix);
        remapRig._effectiveAcceleration = TransformVector2(remapRig._effectiveAcceleration, inPortalMatrix, outPortalMatrix);

        _correctRotation = true;

        if (Vector3.Angle(remapRig.transform.up, Vector3.up) > 10f && PhysicsRig.ballLocoEnabled)
        {
            ToggleBallLoco(false);
            _correctBallLoco = true;
        }

        // Prevent wind sounds caused by teleporting
        if (WindBuffetSFX != null)
        {
            WindBuffetSFX._lastPosition = WindBuffetSFX.transform.position;
        }

        // Scale the player's avatar
        var newScale = outPortalMatrix.lossyScale.y / inPortalMatrix.lossyScale.y;

        if (!Mathf.Approximately(newScale, 1f))
        {
            ScalePlayer(newScale);
        }

        UpdateArt();

        // Teleport grips
        TeleportHands(PhysicsRig.leftHand, PhysicsRig.rightHand, inPortal, outPortal);
    }

    private void UpdateArt()
    {
        PhysicsRig.artOutput.ArtOutputUpdate(PhysicsRig);
        PhysicsRig.artOutput.ArtOutputLateUpdate(PhysicsRig);
    }

    private Vector2 TransformVector2(Vector2 vector, Matrix4x4 inMatrix, Matrix4x4 outMatrix)
    {
        var newVector = new Vector3(vector.x, 0f, vector.y);

        newVector = outMatrix.MultiplyVector(inMatrix.inverse.MultiplyVector(newVector));

        return new Vector2(newVector.x, newVector.z);
    }

    private PendingTransform CreatePendingTransform(Transform transform, Matrix4x4 inMatrix, Matrix4x4 outMatrix)
    {
        var matrix = CalculateTeleportedMatrix(transform.localToWorldMatrix, inMatrix, outMatrix);
        return new PendingTransform()
        {
            transform = transform,
            position = matrix.GetPosition(),
            rotation = matrix.GetRotation(),
        };
    }

    private void ScalePlayer(float factor)
    {
        if (OnScaleEvent != null)
        {
            var shouldScale = OnScaleEvent(this, factor);

            if (!shouldScale)
            {
                ScaleSlots(factor);
                return;
            }
        }

        var newScale = RigManager.avatar.transform.localScale * factor;

        var crate = RigManager.AvatarCrate.Crate;

        var onLoaded = (GameObject avatar) =>
        {
            GameObject instance = GameObject.Instantiate(avatar, CloneCreator.DisabledCloningTransform);
            instance.SetActive(false);

            instance.transform.parent = RigManager.transform;
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = newScale;

            var avatarComponent = instance.GetComponent<Avatar>();
            RigManager.SwitchAvatar(avatarComponent);

            RigManager._avatarCrate = new AvatarCrateReference(crate.Barcode);
            RigManager.onAvatarSwapped?.Invoke();
            RigManager.onAvatarSwapped2?.Invoke(crate.Barcode);

            PlayerRefs.Instance.PlayerBodyVitals.PROPEGATE_SOFT();

            ScaleSlots(factor);
        };

        var asset = crate.MainGameObject.Asset;

        if (asset != null)
        {
            onLoaded(asset);
        }
        else
        {
            crate.LoadAsset(onLoaded);
        }
    }

    private void ScaleSlots(float factor)
    {
        foreach (var slot in Slots)
        {
            var host = slot._weaponHost;

            if (host == null)
            {
                continue;
            }

            var interactableHost = host.TryCast<InteractableHost>();

            if (interactableHost == null)
            {
                continue;
            }

            var marrowEntity = interactableHost.marrowEntity;

            if (marrowEntity == null)
            {
                continue;
            }

            var teleportableEntity = interactableHost.GetComponent<TeleportableEntity>();

            if (teleportableEntity == null)
            {
                continue;
            }

            teleportableEntity.Scale(factor);
        }
    }

    private static void TransferHands(Hand leftHand, Hand rightHand, Portal portal)
    {
        var leftBody = GetTeleportableInHand(leftHand).body;
        var rightBody = GetTeleportableInHand(rightHand).body;

        if (leftBody == rightBody)
        {
            rightBody = null;
        }

        if (leftBody != null)
        {
            leftBody.OverridePortal = portal;
        }

        if (rightBody != null)
        {
            rightBody.OverridePortal = portal;
        }
    }

    private static void TeleportHands(Hand leftHand, Hand rightHand, Portal inPortal, Portal outPortal)
    {
        var leftEntity = GetTeleportableInHand(leftHand).entity;
        var rightEntity = GetTeleportableInHand(rightHand).entity;

        if (leftEntity == rightEntity)
        {
            rightEntity = null;
        }

        if (leftEntity != null)
        {
            leftEntity.Teleport(inPortal, outPortal);
        }

        if (rightEntity != null)
        {
            rightEntity.Teleport(inPortal, outPortal);
        }
    }

    private static (TeleportableBody body, TeleportableEntity entity) GetTeleportableInHand(Hand hand)
    {
        var attachedObject = hand.m_CurrentAttachedGO;

        if (attachedObject == null)
        {
            return (null, null);
        }

        var grip = Grip.Cache.Get(attachedObject);

        if (grip == null)
        {
            return (null, null);
        }

        var entity = grip._marrowEntity;

        if (entity == null)
        {
            grip.ForceDetach(hand);
            return (null, null);
        }

        var teleportableEntity = entity.GetComponent<TeleportableEntity>();

        if (teleportableEntity == null)
        {
            grip.ForceDetach(hand);
            return (null, null);
        }

        var teleportableBody = grip.Host.GetHostGameObject().GetComponent<TeleportableBody>();

        if (teleportableBody == null)
        {
            grip.ForceDetach(hand);
            return (null, null);
        }

        return (teleportableBody, teleportableEntity);
    }
}
