using System;
using System.Linq;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Game.P1Capture
{
    public static class CapturePlayerLoop
    {
        public static void Install(PlayerLoopSystem.UpdateFunction update)
        {
            var loop = Remove(PlayerLoop.GetCurrentPlayerLoop());
            var installed = false;
            loop = Insert(loop, update, ref installed);
            if (!installed) throw new InvalidOperationException("Unity player loop has no LateUpdate script phase.");
            PlayerLoop.SetPlayerLoop(loop);
        }

        public static void Uninstall() => PlayerLoop.SetPlayerLoop(Remove(PlayerLoop.GetCurrentPlayerLoop()));

        public static PlayerLoopSystem Insert(PlayerLoopSystem loop, PlayerLoopSystem.UpdateFunction update, ref bool installed)
        {
            if (loop.subSystemList == null) return loop;
            var children = loop.subSystemList.ToList();
            for (var i = 0; i < children.Count; i++)
            {
                if (!installed && children[i].type == typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate))
                {
                    children.Insert(i + 1, new PlayerLoopSystem { type = typeof(CapturePlayerLoop), updateDelegate = update });
                    installed = true;
                    i++;
                }
                else children[i] = Insert(children[i], update, ref installed);
            }
            loop.subSystemList = children.ToArray();
            return loop;
        }

        public static PlayerLoopSystem Remove(PlayerLoopSystem loop)
        {
            if (loop.subSystemList != null)
                loop.subSystemList = loop.subSystemList.Where(child => child.type != typeof(CapturePlayerLoop)).Select(Remove).ToArray();
            return loop;
        }
    }
}
