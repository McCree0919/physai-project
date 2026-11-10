using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PhysicalAI.Climber
{
    // All poses and vectors are arena-local (relative to the ClimberBody root), so a snapshot restores into any
    // translated or yawed copy of the same arena. Hand-frame quantities (requested/target) are torso-local as in the body.
    [Serializable] public sealed class HandSnapshot
    {
        public Vector3 position,velocity,angularVelocity;public Quaternion rotation;
        public Vector3 requested,target,force;
        public float effort,appliedEffort,cooldown,peak;
        public bool wantsGrab,armed,held;
        // joint=false with held=true is a grip whose joint broke during the last solve and is not yet processed.
        public bool joint;public Vector3 gripAnchor,gripConnectedAnchor;public Quaternion gripRotation;public float gripBreakForce,gripBreakTorque;
        public string capturedSurface="";
    }
    [Serializable] public sealed class BodySnapshot
    {
        public const int CurrentVersion=1;
        public int version=CurrentVersion;public string configurationHash="";
        public Vector3 torsoPosition,torsoVelocity,torsoAngularVelocity;public Quaternion torsoRotation;
        public HandSnapshot[] hands={new HandSnapshot(),new HandSnapshot()};
        public float diagnosticTime;
    }

    public sealed partial class ClimberBody
    {
        public const float SnapshotMaxPenetration=.005f,SnapshotMaxAnchorError=.001f,SnapshotArmTolerance=.01f;
        readonly Quaternion[] gripRotations={Quaternion.identity,Quaternion.identity};
        // Read-only: the static surface a hand currently holds (null when free).
        public Collider GetCapturedSurface(HandId hand) => captured[(int)hand];

        // Episode management only (same level as ResetBody). surfaceKey maps a captured Collider to a stable id.
        public BodySnapshot CaptureSnapshot(Func<Collider,string> surfaceKey)
        {
            var inverse=Quaternion.Inverse(transform.rotation);
            var s=new BodySnapshot {configurationHash=ConfigurationHash(),diagnosticTime=diagnosticTime,
                torsoPosition=transform.InverseTransformPoint(torso.position),torsoRotation=inverse*torso.rotation,
                torsoVelocity=transform.InverseTransformDirection(torso.linearVelocity),torsoAngularVelocity=transform.InverseTransformDirection(torso.angularVelocity)};
            for(int i=0;i<2;i++)
            {
                var h=s.hands[i];var rb=hands[i];
                h.position=transform.InverseTransformPoint(rb.position);h.rotation=inverse*rb.rotation;
                h.velocity=transform.InverseTransformDirection(rb.linearVelocity);h.angularVelocity=transform.InverseTransformDirection(rb.angularVelocity);
                h.requested=requested[i];h.target=targets[i];h.force=transform.InverseTransformDirection(forces[i]);
                h.effort=effort[i];h.appliedEffort=appliedEffort[i];h.cooldown=cooldown[i];h.peak=peak[i];
                h.wantsGrab=wantsGrab[i];h.armed=armed[i];h.held=held[i];h.joint=grips[i]!=null;
                h.capturedSurface=captured[i]!=null?surfaceKey(captured[i]):"";
                if(h.joint)
                {
                    h.gripAnchor=grips[i].anchor;h.gripConnectedAnchor=transform.InverseTransformPoint(grips[i].connectedAnchor);
                    h.gripRotation=inverse*gripRotations[i];h.gripBreakForce=grips[i].breakForce;h.gripBreakTorque=grips[i].breakTorque;
                }
            }
            return s;
        }

        // Returns null when the snapshot can be restored into this arena, otherwise the rejection reason. Never mutates state.
        public string ValidateSnapshot(BodySnapshot s,Func<string,Collider> resolve)
        {
            if(s==null||s.hands==null||s.hands.Length!=2||s.hands[0]==null||s.hands[1]==null)return "Malformed";
            if(s.version!=BodySnapshot.CurrentVersion)return "Version";
            if(s.configurationHash!=ConfigurationHash())return "BodyConfiguration";
            if(!IsFinite(s.torsoPosition)||!IsFinite(s.torsoVelocity)||!IsFinite(s.torsoAngularVelocity)||!IsFinite(s.torsoRotation))return "Nonfinite";
            Vector3 torsoWorld=transform.TransformPoint(s.torsoPosition);Quaternion torsoRotation=transform.rotation*s.torsoRotation;
            float worst=Penetration(torso.GetComponent<Collider>(),torsoWorld,torsoRotation);
            for(int i=0;i<2;i++)
            {
                var h=s.hands[i];
                if(!IsFinite(h.position)||!IsFinite(h.velocity)||!IsFinite(h.angularVelocity)||!IsFinite(h.rotation)||!IsFinite(h.requested)||!IsFinite(h.target)||!IsFinite(h.force))return "Nonfinite";
                if(float.IsNaN(h.effort+h.appliedEffort+h.cooldown)||float.IsInfinity(h.effort+h.appliedEffort+h.cooldown))return "Nonfinite";
                Vector3 handWorld=transform.TransformPoint(h.position);Quaternion handRotation=transform.rotation*h.rotation;
                if(Vector3.Distance(handWorld,torsoWorld+torsoRotation*shoulders[i])>ArmLength+SnapshotArmTolerance)return "ArmLength"+i;
                Collider surface=null;
                if(!string.IsNullOrEmpty(h.capturedSurface)){surface=resolve(h.capturedSurface);if(surface==null)return "UnknownSurface"+i;}
                if(h.joint)
                {
                    if(!h.held||surface==null)return "GripWithoutSurface"+i;
                    if(!IsFinite(h.gripAnchor)||!IsFinite(h.gripConnectedAnchor)||!IsFinite(h.gripRotation))return "Nonfinite";
                    Vector3 point=transform.TransformPoint(h.gripConnectedAnchor);
                    Vector3 anchorWorld=handWorld+handRotation*Vector3.Scale(h.gripAnchor,hands[i].transform.lossyScale);
                    if(Vector3.Distance(anchorWorld,point)>SnapshotMaxAnchorError)return "GripAnchor"+i;
                    if(Vector3.Distance(surface.ClosestPoint(point),point)>.111f)return "GripTooFar"+i;
                }
                worst=Mathf.Max(worst,Penetration(hands[i].GetComponent<Collider>(),handWorld,handRotation));
            }
            return worst>SnapshotMaxPenetration?"Penetration":null;
        }

        // Rejects (throws, state untouched) unless ValidateSnapshot passes; see LAYER3_SNAPSHOT_DESIGN section 3 for the order.
        public void RestoreSnapshot(BodySnapshot s,Func<string,Collider> resolve)
        {
            string reason=ValidateSnapshot(s,resolve);
            if(reason!=null)throw new ArgumentException("Body snapshot rejected: "+reason);
            for(int i=0;i<2;i++)
            {
                // Immediate removal of every grip joint, including ones Release/ResetBody already scheduled with a deferred
                // Destroy (breakForce 0): those would otherwise take part in one more solve against the restored hand.
                foreach(var stale in hands[i].GetComponents<FixedJoint>())DestroyImmediate(stale);
                grips[i]=null;held[i]=false;captured[i]=null;
            }
            Vector3 torsoWorld=transform.TransformPoint(s.torsoPosition);Quaternion torsoRotation=transform.rotation*s.torsoRotation;
            torso.transform.SetPositionAndRotation(torsoWorld,torsoRotation);torso.position=torsoWorld;torso.rotation=torsoRotation;
            for(int i=0;i<2;i++)
            {
                var h=s.hands[i];Vector3 position=transform.TransformPoint(h.position);
                // A FixedJoint locks the hand orientation it sees at creation: create it at the original grip-time rotation.
                Quaternion rotation=transform.rotation*(h.joint?h.gripRotation:h.rotation);
                hands[i].transform.SetPositionAndRotation(position,rotation);hands[i].position=position;hands[i].rotation=rotation;
            }
            Physics.SyncTransforms();
            for(int i=0;i<2;i++)
            {
                var h=s.hands[i];if(!h.joint)continue;
                var grip=hands[i].gameObject.AddComponent<FixedJoint>();
                grip.connectedBody=null;grip.autoConfigureConnectedAnchor=false;
                grip.anchor=h.gripAnchor;grip.connectedAnchor=transform.TransformPoint(h.gripConnectedAnchor);
                grip.breakForce=h.gripBreakForce;grip.breakTorque=h.gripBreakTorque;grip.enableCollision=false;
                grips[i]=grip;gripRotations[i]=transform.rotation*h.gripRotation;
            }
            for(int i=0;i<2;i++)
            {
                Quaternion rotation=transform.rotation*s.hands[i].rotation;
                hands[i].transform.rotation=rotation;hands[i].rotation=rotation;
            }
            Physics.SyncTransforms();
            ApplyRotationLocks();
            torso.linearVelocity=transform.TransformDirection(s.torsoVelocity);torso.angularVelocity=transform.TransformDirection(s.torsoAngularVelocity);torso.WakeUp();
            for(int i=0;i<2;i++)
            {
                var h=s.hands[i];
                hands[i].linearVelocity=transform.TransformDirection(h.velocity);hands[i].angularVelocity=transform.TransformDirection(h.angularVelocity);hands[i].WakeUp();
                requested[i]=h.requested;targets[i]=h.target;forces[i]=transform.TransformDirection(h.force);
                effort[i]=h.effort;appliedEffort[i]=h.appliedEffort;cooldown[i]=h.cooldown;peak[i]=h.peak;
                wantsGrab[i]=h.wantsGrab;armed[i]=h.armed;held[i]=h.held;
                captured[i]=string.IsNullOrEmpty(h.capturedSurface)?null:resolve(h.capturedSurface);
            }
            diagnosticTime=s.diagnosticTime;
        }

        float Penetration(Collider part,Vector3 position,Quaternion rotation)
        {
            float worst=0;
            foreach(var other in Physics.OverlapSphere(position,1.5f,~0,QueryTriggerInteraction.Ignore))
            {
                if(other.attachedRigidbody!=null||!other.transform.IsChildOf(transform))continue;
                if(Physics.ComputePenetration(part,position,rotation,other,other.transform.position,other.transform.rotation,out _,out float distance))worst=Mathf.Max(worst,distance);
            }
            return worst;
        }
        static bool IsFinite(Quaternion q) => !float.IsNaN(q.x+q.y+q.z+q.w) && !float.IsInfinity(q.x+q.y+q.z+q.w) && q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.5f;

        // SHA-256 of every body parameter that changes physical behaviour; a snapshot never restores across differing bodies.
        public string ConfigurationHash()
        {
            var ci=CultureInfo.InvariantCulture;var text=new StringBuilder();
            void F(float value)=>text.Append(value.ToString("R",ci)).Append('|');
            void V(Vector3 value){F(value.x);F(value.y);F(value.z);}
            void Body(Rigidbody rb)
            {
                F(rb.mass);F(rb.linearDamping);F(rb.angularDamping);text.Append(rb.solverIterations).Append('|').Append(rb.solverVelocityIterations).Append('|').Append(rb.collisionDetectionMode).Append('|');
                V(rb.transform.localScale);text.Append(rb.GetComponent<Collider>().GetType().Name).Append('|');
            }
            F(ArmLength);F(PullForce);F(FreeForce);F(Spring);F(Damper);F(TargetSpeed);
            text.Append(LockRotationX).Append(LockRotationY).Append(LockRotationZ).Append('|').Append(GrabbableMask.value).Append('|');
            foreach(var shoulder in shoulders)V(shoulder);
            Body(torso);foreach(var hand in hands)Body(hand);
            foreach(var arm in arms)
            {
                F(arm.linearLimit.limit);F(arm.linearLimit.contactDistance);V(arm.anchor);V(arm.connectedAnchor);F(arm.breakForce);F(arm.breakTorque);
                text.Append(arm.xMotion).Append(arm.yMotion).Append(arm.zMotion).Append(arm.angularXMotion).Append(arm.angularYMotion).Append(arm.angularZMotion).Append('|');
            }
            using(var sha=SHA256.Create())
            {
                var digest=sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));var hex=new StringBuilder(64);
                foreach(byte b in digest)hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
