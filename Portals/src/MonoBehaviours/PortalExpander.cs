﻿using System;

using Il2CppInterop.Runtime.InteropTypes.Fields;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Interaction;

using MelonLoader;

using UnityEngine;

namespace Portals.MonoBehaviours;

[RegisterTypeInIl2Cpp]
public class PortalExpander : MonoBehaviour
{
    public PortalExpander(IntPtr intPtr) : base(intPtr) { }

    #region FIELD INJECTION
    public Il2CppReferenceField<BoxCollider> rightSideCollider;
    public Il2CppReferenceField<BoxCollider> topSideCollider;
    public Il2CppReferenceField<BoxCollider> leftSideCollider;
    public Il2CppReferenceField<BoxCollider> bottomSideCollider;

    public Il2CppReferenceField<BoxCollider> rightFrontCollider;
    public Il2CppReferenceField<BoxCollider> topFrontCollider;
    public Il2CppReferenceField<BoxCollider> leftFrontCollider;
    public Il2CppReferenceField<BoxCollider> bottomFrontCollider;

    public Il2CppReferenceField<BoxCollider> rightBackCollider;
    public Il2CppReferenceField<BoxCollider> topBackCollider;
    public Il2CppReferenceField<BoxCollider> leftBackCollider;
    public Il2CppReferenceField<BoxCollider> bottomBackCollider;
    #endregion

    #region FIELDS
    private BoxCollider[] _colliders = null;

    private BoxCollider[] _sideColliders = null;
    private BoxCollider[] _frontColliders = null;
    private BoxCollider[] _backColliders = null;
    #endregion

    #region PROPERTIES
    public BoxCollider RightSideCollider => rightSideCollider.Get();
    public BoxCollider TopSideCollider => topSideCollider.Get();
    public BoxCollider LeftSideCollider => leftSideCollider.Get();
    public BoxCollider BottomSideCollider => bottomSideCollider.Get();

    public BoxCollider RightFrontCollider => rightFrontCollider.Get();
    public BoxCollider TopFrontCollider => topFrontCollider.Get();
    public BoxCollider LeftFrontCollider => leftFrontCollider.Get();
    public BoxCollider BottomFrontCollider => bottomFrontCollider.Get();

    public BoxCollider RightBackCollider => rightBackCollider.Get();
    public BoxCollider TopBackCollider => topBackCollider.Get();
    public BoxCollider LeftBackCollider => leftBackCollider.Get();
    public BoxCollider BottomBackCollider => bottomBackCollider.Get();
    #endregion

    #region METHODS
    public void ToggleCollision(bool enabled)
    {
        if (_colliders == null)
        {
            return;
        }

        foreach (var collider in _colliders)
        {
            collider.enabled = enabled;
        }
    }

    public void Expand()
    {
        ToggleCollision(false);

        foreach (var collider in _sideColliders)
        {
            ExpandSide(collider);
        }

        foreach (var collider in _frontColliders)
        {
            ExpandNormal(collider);
        }

        foreach (var collider in _backColliders)
        {
            ExpandNormal(collider);
        }
    }

    private void ExpandSide(BoxCollider collider)
    {
        var start = collider.transform.position;
        var direction = collider.transform.right;

        var normal = collider.transform.forward;

        Vector3 position = start;

        bool expanded = false;

        for (var i = 0; i < 30; i++)
        {
            var newPosition = position + direction * 0.1f;

            var above = newPosition + normal * 0.1f;
            var below = newPosition - normal * 0.1f;

            if (!Physics.Linecast(above, below, PortalConstants.HitMask, QueryTriggerInteraction.Ignore))
            {
                break;
            }

            position = newPosition;

            expanded = true;
        }

        if (!expanded)
        {
            collider.enabled = false;
            return;
        }

        collider.enabled = true;

        var vector = collider.transform.InverseTransformVector(position - start);
        float distance = vector.magnitude;

        var size = collider.size;
        size.x = distance;
        size.y = 1f + (distance * 2f);

        var center = collider.center;
        center.x = distance * 0.5f;

        collider.size = size;
        collider.center = center;
    }

    private void ExpandNormal(BoxCollider collider)
    {
        var direction = collider.transform.right;

        var rayOffset = direction * 0.01f;
        var selfPosition = transform.position + rayOffset;
        var fromTo = -collider.transform.forward;

        RaycastHit offsetInfo = default;
        bool foundOffsetRay = false;

        foreach (var hit in Physics.RaycastAll(selfPosition, fromTo, Vector3.Scale(transform.lossyScale, fromTo).magnitude * 2f, PortalConstants.HitMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody)
            {
                continue;
            }

            if (hit.collider.GetComponentInParent<Portal>())
            {
                continue;
            }

            if (foundOffsetRay && hit.distance >= offsetInfo.distance)
            {
                continue;
            }

            offsetInfo = hit;
            foundOffsetRay = true;
        }

        if (!foundOffsetRay)
        {
            collider.enabled = false;
            return;
        }

        collider.transform.position = offsetInfo.point - rayOffset;

        var start = collider.transform.position;

        var normal = collider.transform.forward;

        Vector3 position = start;

        bool expanded = false;

        for (var i = 0; i < 30; i++)
        {
            var newPosition = position + direction * 0.1f;

            var above = newPosition + normal * 0.1f;
            var below = newPosition - normal * 0.1f;

            if (!Physics.Linecast(above, below, PortalConstants.HitMask, QueryTriggerInteraction.Ignore))
            {
                break;
            }

            position = newPosition;

            expanded = true;
        }

        if (!expanded)
        {
            collider.enabled = false;
            return;
        }

        collider.enabled = true;

        var vector = collider.transform.InverseTransformVector(position - start);
        float distance = vector.magnitude;

        var size = collider.size;
        size.x = distance;
        size.y = 1f + (distance * 2f);

        var center = collider.center;
        center.x = distance * 0.5f;

        collider.size = size;
        collider.center = center;
    }

    public void IgnoreCollision(Teleportable teleportable, float sign = 1f, bool ignore = true)
    {
        try
        {
            var normalColliders = sign > 0f ? _frontColliders : _backColliders;

            foreach (var body in teleportable.Bodies)
            {
                foreach (var collider in _sideColliders)
                {
                    body.MarrowBody.IgnoreCollision(collider, ignore);
                }

                foreach (var collider in normalColliders)
                {
                    body.MarrowBody.IgnoreCollision(collider, ignore);
                }
            }
        }
        catch { }
    }

    public void IgnoreCollision(Teleportable teleportable, bool ignore = true)
    {
        try
        {
            foreach (var body in teleportable.Bodies)
            {
                foreach (var collider in _colliders)
                {
                    body.MarrowBody.IgnoreCollision(collider, ignore);
                }
            }
        }
        catch { }
    }

    private void OnTeleportableEnabled(Teleportable teleportable)
    {
        IgnoreCollision(teleportable, true);
    }

    private void OnTeleportableDisabled(Teleportable teleportable)
    {
        IgnoreCollision(teleportable, false);
    }

    private void IgnoreExistingTeleportables()
    {
        foreach (var teleportable in Teleportable.Teleportables)
        {
            IgnoreCollision(teleportable, true);
        }
    }

    private void CollectColliders()
    {
        _colliders = new BoxCollider[]
        {
            RightSideCollider,
            TopSideCollider,
            LeftSideCollider,
            BottomSideCollider,

            RightFrontCollider,
            TopFrontCollider,
            LeftFrontCollider,
            BottomFrontCollider,

            RightBackCollider,
            TopBackCollider,
            LeftBackCollider,
            BottomBackCollider,
        };

        _sideColliders = new BoxCollider[]
        {
            RightSideCollider,
            TopSideCollider,
            LeftSideCollider,
            BottomSideCollider,
        };

        _frontColliders = new BoxCollider[]
        {
            RightFrontCollider,
            TopFrontCollider,
            LeftFrontCollider,
            BottomFrontCollider,
        };

        _backColliders = new BoxCollider[]
        {
            RightBackCollider,
            TopBackCollider,
            LeftBackCollider,
            BottomBackCollider,
        };
    }

    private void OverrideLayers()
    {
        var layer = (int)MarrowLayers.Football;

        // I'd rather not override this, but this is the closest layer to what I want that can't get step detected
        // Also it doesn't really affect anything as far as I can tell, since the feet and knee joint disables collision
        Physics.IgnoreLayerCollision(layer, layer, false);

        // Football layer is used to prevent the expanded colliders from blocking force grabs and causing stepping
        foreach (var collider in _colliders)
        {
            collider.gameObject.layer = layer;
        }
    }

    private void OverrideBullets()
    {
        // Bullets shouldn't interact with these colliders
        foreach (var collider in _colliders)
        {
            collider.gameObject.AddComponent<BallisticPassthrough>();
        }
    }
    #endregion

    #region UNITY
    private void Awake()
    {
        CollectColliders();

        OverrideLayers();
        OverrideBullets();

        Teleportable.OnTeleportableEnabled += OnTeleportableEnabled;
        Teleportable.OnTeleportableDisabled += OnTeleportableDisabled;

        IgnoreExistingTeleportables();
    }

    private void OnDestroy()
    {
        Teleportable.OnTeleportableEnabled -= OnTeleportableEnabled;
        Teleportable.OnTeleportableDisabled -= OnTeleportableDisabled;
    }
    #endregion
}
