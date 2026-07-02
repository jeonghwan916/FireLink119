using Fusion;
using UnityEngine;

namespace FireLink119.Network
{
    public struct VrAvatarNetworkInput : INetworkInput
    {
        // Fusion input은 입력 권한을 가진 플레이어가 매 tick 제출하고, StateAuthority가 FixedUpdateNetwork에서 읽는다.
        // 위치/회전은 아바타 루트 자체를 맞추기 위해 월드 좌표로 보낸다.
        public Vector3 AvatarPosition;
        public Quaternion AvatarRotation;

        // 손은 본을 직접 동기화하지 않고 IK Target을 동기화한다.
        // Target 값은 아바타 루트 기준 로컬 좌표라서 플레이어 스폰 위치가 달라도 같은 자세로 복원된다.
        public Vector3 LeftHandLocalPosition;
        public Quaternion LeftHandLocalRotation;
        public Vector3 RightHandLocalPosition;
        public Quaternion RightHandLocalRotation;

        // 이동 애니메이션은 Blend Tree 파라미터만 보내고, 실제 재생은 각 클라이언트의 Animator가 처리한다.
        public Vector2 MoveBlend;
        public NetworkBool IsMoving;
        public NetworkBool IsSprinting;
    }
}
