﻿using HarmonyLib;

using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.AI;
using Il2CppSLZ.Marrow.Data;

using Portals.MonoBehaviours;

using UnityEngine;

namespace Portals.Patching;

[HarmonyPatch(typeof(Projectile))]
public static class ProjectilePatches
{
    public static bool TeleportOnUpdate { get; set; } = false;
    public static Portal TargetInPortal { get; set; } = null;
    public static Portal TargetOutPortal { get; set; } = null;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Projectile.SetBulletObject))]
    public static void SetBulletObjectPrefix(Projectile __instance, ProjectileData data, Transform startTransform, Vector3 locPos, Quaternion locRot, Rigidbody EmittingRigidbody, TriggerRefProxy proxy)
    {
        if (data == null)
        {
            return;
        }

        var gun = startTransform.GetComponentInParent<Gun>();

        if (gun == null)
        {
            return;
        }

        var entity = gun.GetComponent<TeleportableEntity>();

        if (entity == null || !entity.HasPortals)
        {
            return;
        }

        float sign = entity.GetPortalSign(entity.InPortal, startTransform.position);

        if (entity.PassedThrough(entity.EnterSign, sign))
        {
            TeleportOnUpdate = true;
            TargetInPortal = entity.InPortal;
            TargetOutPortal = entity.OutPortal;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Projectile.Update))]
    public static void UpdatePrefix(Projectile __instance)
    {
        if (TeleportOnUpdate)
        {
            TeleportOnUpdate = false;

            Teleport(__instance, TargetInPortal, TargetOutPortal);

            TargetInPortal = null;
            TargetOutPortal = null;

            return;
        }

        var start = __instance.transform.position;
        var end = __instance.transform.position + __instance._direction * __instance.currentSpeed * Time.deltaTime;

        var lineCast = Physics.Linecast(start, end, out var hitInfo, 1 << PortalConstants.PortalLayer, QueryTriggerInteraction.Collide);

        if (lineCast && hitInfo.collider.isTrigger)
        {
            var portal = hitInfo.collider.GetComponentInParent<Portal>();

            if (portal != null && portal.OtherPortal != null)
            {
                Teleport(__instance, portal, portal.OtherPortal);
            }
        }
    }

    private static void Teleport(Projectile projectile, Portal inPortal, Portal outPortal)
    {
        var inMatrix = inPortal.PortalEnterMatrix;
        var outMatrix = outPortal.PortalExitMatrix;

        var matrix = Matrix4x4.TRS(projectile.transform.position, Quaternion.LookRotation(projectile._direction), Vector3.one);

        var matrixInPortal = inMatrix.inverse * matrix;

        var matrixInPortalPosition = matrixInPortal.GetPosition();
        var matrixInPortalForward = matrixInPortal.rotation * Vector3.forward;

        var plane = new Plane(Vector3.forward, Vector3.zero);
        var planeRay = new Ray(matrixInPortalPosition, matrixInPortalForward * Mathf.Sign(matrixInPortalPosition.z));

        if (plane.Raycast(planeRay, out var planeEnter))
        {
            matrixInPortalPosition += planeRay.direction * planeEnter;
        }

        matrixInPortal = Matrix4x4.TRS(matrixInPortalPosition, matrixInPortal.rotation, matrixInPortal.lossyScale);

        matrix = outMatrix * matrixInPortal;

        projectile.transform.position = matrix.GetPosition();
        projectile._direction = matrix.rotation * Vector3.forward;

        var scale = matrix.lossyScale;

        projectile.Mass *= scale.x * scale.y * scale.z;
        projectile.currentSpeed *= scale.z;

        projectile.trail.Clear();
    }
}
