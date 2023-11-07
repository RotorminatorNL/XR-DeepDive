using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerSwordDetection : MonoBehaviour
{
    public delegate void SwordDetection();
    public event SwordDetection Detected;

    [SerializeField] private LayerMask triggerLayer;
    [SerializeField] private Animator animator;
    [SerializeField, Tooltip("The sword should (true) or shouldn't (false) be detected during animations below.")] private bool whiteListAnims;
    [SerializeField] private string[] animNames;

    private void OnTriggerEnter(Collider other)
    {
        bool clipInsideList = false;

        foreach (var name in animNames)
        {
            if (animator.GetCurrentAnimatorClipInfo(0)[0].clip.name == name) clipInsideList = true;
        }

        bool shouldDetect = false;

        if (whiteListAnims && clipInsideList) shouldDetect = true;
        else if (!whiteListAnims && !clipInsideList) shouldDetect = true;

        if (shouldDetect && (triggerLayer.value & (1 << other.gameObject.layer)) > 0) Detected();
    }
}
