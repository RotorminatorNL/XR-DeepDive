using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IKFootSolver : MonoBehaviour
{
    [SerializeField] private Transform firstParent;
    private float firstParentInitialYPos;

    private float footInitialYPos;
    private float footCurrentYPosOffset;

    private void Start()
    {
        firstParentInitialYPos = firstParent.localPosition.y;
        footInitialYPos = transform.localPosition.y;
    }

    private void Update()
    {
        float footNewYPosOffset = (firstParentInitialYPos - firstParent.localPosition.y) / firstParent.localScale.y;
        if (footNewYPosOffset != footCurrentYPosOffset)
        {
            footCurrentYPosOffset = footNewYPosOffset;
            float newY = footInitialYPos + footCurrentYPosOffset;
            transform.localPosition = new Vector3(transform.localPosition.x, newY, transform.localPosition.z);
        }
    }
}