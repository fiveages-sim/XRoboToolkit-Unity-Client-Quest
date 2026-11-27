using Oculus.Interaction.Locomotion;
using UnityEngine;

/// <summary>
/// Quest teleop: sticks are data-only. Meta ISDK <c>OVRInteractionComprehensive</c>
/// ships stick turn/teleport that rotates <c>OVRCameraRig</c> (feels like panel↔hand coupling).
/// Disable that locomotion; keep controller rays for UI. B / Meta / MoveButton UX unchanged.
/// </summary>
public static class DisableMetaStickLocomotion
{
    private static readonly string[] KillNames =
    {
        "Locomotion",
        "LocomotionControllerInteractorGroup",
        "ControllerTurnerInteractor",
        "TeleportControllerInteractor",
        "ControllerSlideInteractor",
    };

    public static void Apply()
    {
        int disabled = 0;

        foreach (var loco in Object.FindObjectsOfType<PlayerLocomotor>(true))
        {
            if (loco == null)
            {
                continue;
            }

            loco.enabled = false;
            if (loco.gameObject.activeSelf)
            {
                loco.gameObject.SetActive(false);
                disabled++;
                Debug.Log("[DisableMetaStickLocomotion] disabled PlayerLocomotor on " +
                          loco.gameObject.name);
            }
        }

        foreach (var turner in Object.FindObjectsOfType<LocomotionAxisTurnerInteractor>(true))
        {
            if (turner == null)
            {
                continue;
            }

            turner.enabled = false;
            if (turner.gameObject.activeSelf)
            {
                turner.gameObject.SetActive(false);
                disabled++;
            }
        }

        // Nested under OVRInteractionComprehensive (also catch by exact name).
        Transform root = null;
        var comprehensive = GameObject.Find("OVRInteractionComprehensive");
        if (comprehensive != null)
        {
            root = comprehensive.transform;
        }

        if (root != null)
        {
            disabled += DisableNamedDescendants(root);
        }
        else
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t != null && ShouldKill(t.name) && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    disabled++;
                    Debug.Log("[DisableMetaStickLocomotion] disabled " + t.name);
                }
            }
        }

        Debug.Log("[DisableMetaStickLocomotion] done, actions=" + disabled);
    }

    private static int DisableNamedDescendants(Transform root)
    {
        int n = 0;
        var stack = new System.Collections.Generic.Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Transform cur = stack.Pop();
            for (int i = 0; i < cur.childCount; i++)
            {
                stack.Push(cur.GetChild(i));
            }

            if (!ShouldKill(cur.name) || !cur.gameObject.activeSelf)
            {
                continue;
            }

            cur.gameObject.SetActive(false);
            n++;
            Debug.Log("[DisableMetaStickLocomotion] disabled " + cur.name);
        }

        return n;
    }

    private static bool ShouldKill(string name)
    {
        for (int i = 0; i < KillNames.Length; i++)
        {
            if (name == KillNames[i])
            {
                return true;
            }
        }

        return false;
    }
}
