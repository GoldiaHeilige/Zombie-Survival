using Fusion;
using UnityEngine;

/// <summary>
/// Input gửi mỗi tick. Các trường sau đây:
/// - held: sprint, fire, ads
/// - edge: jump, reload, interact, replace, drop, prev, next, slot1, slot2
/// </summary>
public struct PlayerInputData : INetworkInput
{
    public Vector2 move;
    public Vector2 look;

    public NetworkBool jump;      // edge
    public NetworkBool sprint;    // held
    public NetworkBool crouch;    // held
    public NetworkBool fire;      // held
    public NetworkBool reload;    // edge
    public NetworkBool ads;       // held

    public NetworkBool interact;  // edge
    public NetworkBool buy;
    public NetworkBool replace;   // edge
    public NetworkBool drop;      // edge
    public NetworkBool reviveHeld;

    public NetworkBool prev;      // edge
    public NetworkBool next;      // edge
    public NetworkBool slot1;     // edge
    public NetworkBool slot2;     // edge

    public NetworkBool spectatePrev; // edge – cycle lùi (chuột phải)
    public NetworkBool spectateNext; // edge – cycle tới (chuột trái)

    public NetworkBool pause;

    public float viewYaw;
    public float viewPitch;
}
