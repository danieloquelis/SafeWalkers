using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sample {
public class GhostScript : MonoBehaviour
{
    private Animator Anim;
    private Vector3 MoveDirection = Vector3.zero;
    // Cache hash values
    private static readonly int IdleState = Animator.StringToHash("Base Layer.idle");
    private static readonly int MoveState = Animator.StringToHash("Base Layer.move");
    private static readonly int SurprisedState = Animator.StringToHash("Base Layer.surprised");
    private static readonly int AttackState = Animator.StringToHash("Base Layer.attack_shift");
    private static readonly int DissolveState = Animator.StringToHash("Base Layer.dissolve");
    private static readonly int AttackTag = Animator.StringToHash("Attack");
    // dissolve
    [SerializeField] private SkinnedMeshRenderer[] MeshR;
    private float Dissolve_value = 1;
    private bool DissolveFlg = false;
    private const int maxHP = 3;

    // moving speed
    [SerializeField] private float Speed = 4;

    void Start()
    {
        Anim = this.GetComponent<Animator>();
    }

    void Update()
    {
        STATUS();
    }

    //---------------------------------------------------------------------
    // character status
    //---------------------------------------------------------------------
    private const int Dissolve = 1;
    private const int Attack = 2;
    private const int Surprised = 3;
    private Dictionary<int, bool> PlayerStatus = new Dictionary<int, bool>
    {
        {Dissolve, false },
        {Attack, false },
        {Surprised, false },
    };
    //------------------------------
    private void STATUS ()
    {
        // during attacking
        if(Anim.GetCurrentAnimatorStateInfo(0).tagHash == AttackTag)
        {
            PlayerStatus[Attack] = true;
        }
        else if(Anim.GetCurrentAnimatorStateInfo(0).tagHash != AttackTag)
        {
            PlayerStatus[Attack] = false;
        }
        // during damaging
        if(Anim.GetCurrentAnimatorStateInfo(0).fullPathHash == SurprisedState)
        {
            PlayerStatus[Surprised] = true;
        }
        else if(Anim.GetCurrentAnimatorStateInfo(0).fullPathHash != SurprisedState)
        {
            PlayerStatus[Surprised] = false;
        }
    }
    private void MOVE_Velocity (Vector3 velocity, Vector3 rot)
    {
        MoveDirection = new Vector3 (velocity.x, MoveDirection.y, velocity.z);
        MoveDirection.x = 0;
        MoveDirection.z = 0;
        this.transform.rotation = Quaternion.Euler(rot);
    }

    private void Damage ()
    {
        // Damaged by outside field.
        if(Input.GetKeyUp(KeyCode.S))
        {
            Anim.CrossFade(SurprisedState, 0.1f, 0, 0);
        }
    }
}
}