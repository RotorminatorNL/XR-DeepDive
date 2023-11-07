using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestDummy : MonoBehaviour
{
    [SerializeField] private LayerMask triggerLayer;
    [SerializeField] private float timeBetweenAttacks = 5;
    [SerializeField] private PlayerSwordDetection playerSwordBlock;
    [SerializeField] private PlayerSwordDetection playerSwordAttack;
    [SerializeField] private float timeBetweenPlayerAttacks = 1;
    [SerializeField, Range(0, 1)] private float blockChance = 0.5f;

    private Animator animator;
    bool playerInRange = false;
    bool waitingOnPermission = false;
    bool permissionToAttack = false;
    bool playerAttacked = false;

    private void Start()
    {
        playerSwordAttack.Detected += HandlePlayerAttack;
        playerSwordBlock.Detected += HandlePlayerBlock;
        animator = GetComponentInChildren<Animator>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if ((triggerLayer.value & (1 << other.gameObject.layer)) > 0) playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if ((triggerLayer.value & (1 << other.gameObject.layer)) > 0) playerInRange = false;
    }

    private void Update()
    {
        if (!playerInRange) return;

        if (!waitingOnPermission)
        {
            waitingOnPermission = true;
            StartCoroutine(GettingPermissionToAttack());
        }

        if (permissionToAttack)
        {
            animator.SetTrigger("Attack");
            waitingOnPermission = false;
            permissionToAttack = false;
        }
    }
    
    private IEnumerator GettingPermissionToAttack()
    {
        yield return new WaitForSeconds(timeBetweenAttacks);
        permissionToAttack = true;
    }

    private void HandlePlayerAttack()
    {
        if (!playerAttacked)
        {
            animator.ResetTrigger("Blocked");
            if (Random.Range(0f, 1f) <= blockChance) animator.SetTrigger("Block");
            else animator.SetTrigger("Hit");

            playerAttacked = true;
            StartCoroutine(RecoverFromPlayerAttack());
        }
    }

    private IEnumerator RecoverFromPlayerAttack()
    {
        yield return new WaitForSeconds(timeBetweenPlayerAttacks);
        playerAttacked = false;
    }

    private void HandlePlayerBlock()
    {
        animator.SetTrigger("Blocked");
    }
}
