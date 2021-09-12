using UnityEngine;
using System.Collections.Generic;
using System;
using Oxide.Core;
using ProtoBuf;

namespace Oxide.Plugins
{
    [Info("AutoElevators", "bmgjet", "1.0.0")]
    [Description("Replaces elevators placed in rust with with working one.")]

    public class AutoElevators : RustPlugin
    {
        private static SaveData _data;
        private const string USE_PERM = "AutoElevators.use";
        private List<uint> Up = new List<uint>();
        public List<Esettings> ElevatorUp = new List<Esettings>();

        class SaveData
        {
            public List<Esettings> ElevatorUp = new List<Esettings>();
        }

        private void WriteSaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private void Init()
        {
            permission.RegisterPermission(USE_PERM, this);
        }

        void OnServerInitialized()
        {
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(Name))
            {
                Interface.Oxide.DataFileSystem.GetDatafile(Name).Save();
            }
            _data = Interface.Oxide.DataFileSystem.ReadObject<SaveData>(Name);
            if (_data == null)
            {
                WriteSaveData();
            }
            timer.Once(5f, () => {
                ReloadData();
            });
        }

        void Unload()
        {
            ClearElevators();
            try
            {
                _data.ElevatorUp = ElevatorUp;
                WriteSaveData();
                _data = null;
            }
            catch { }
        }

        public void ReloadData()
        {
            ClearElevators();
            //create new elevators
            bool flag = false;
            List<uint> update = new List<uint>();
            foreach (PrefabData pd in World.Serialization.world.prefabs)
            {
                if (pd.id == 3845190333)
                {
                    Elevator el = CreateElevator(pd.position, pd.rotation);
                    if (el == null)
                    {
                        Puts("Fault");
                    }
                    foreach (Esettings es in _data.ElevatorUp)
                    {
                        //Check for settings
                        if (es.pos == el.transform.position)
                        {
                            //create with settings
                            update.Add(el.net.ID);
                            _data.ElevatorUp.Add(new Esettings(el.transform.position, el.transform.rotation.ToEuler(), el.net.ID, es.Floors, es.ReturnTime, es.AutoReturn));
                            flag = true;
                            break;
                        }
                    }
                    if (!flag)
                    {
                        //create new
                        _data.ElevatorUp.Add(new Esettings(el.transform.position, el.transform.rotation.ToEuler(), el.net.ID));
                    }
                }
            }
            //clean up
            if (flag)
            {
                int count = _data.ElevatorUp.Count;
                for (int c = 0; c < count; c++)
                {
                    if (!update.Contains(_data.ElevatorUp[c].netid))
                    {
                        _data.ElevatorUp.Remove(_data.ElevatorUp[c]);
                        c--;
                        count--;
                    }
                }
            }
            Puts("ReloadData");
            WriteSaveData();
            ElevatorUp = _data.ElevatorUp;
        }

        void resetdata()
        {
            Puts("Resettings datafile");
            _data.ElevatorUp.Clear();
            WriteSaveData();
            ReloadData();
        }

        public void ClearElevators()
        {
            //Delete any exsisting elevators
            foreach (BaseNetworkable bn in BaseNetworkable.serverEntities)
            {
                if (bn.prefabID == 3845190333)
                {
                    bn.Kill();
                }
            }
        }

        private List<Elevator> FindElevator(Vector3 pos, float radius)
        {
            var hits = Physics.SphereCastAll(pos, 0.5f, Vector3.down);
            var x = new List<Elevator>();
            foreach (var hit in hits)
            {
                var entity = hit.GetEntity()?.GetComponent<Elevator>();
                if (entity && !x.Contains(entity))
                    x.Add(entity);
            }
            return x;
        }

        public Elevator CreateElevator(Vector3 pos, Vector3 rot)
        {
            //Recreate elevators that are workable
            Elevator newElevator = GameManager.server.CreateEntity("assets/prefabs/deployable/elevator/static/elevator.static.prefab", pos + new Vector3(0, -1, 0), new Quaternion(rot.x, rot.y, rot.z, 0), true) as Elevator;
            newElevator.Spawn();
            newElevator.SetFlag(BaseEntity.Flags.Reserved1, true, false, true);
            newElevator.SetFlag(Elevator.Flag_HasPower, true);
            return newElevator;
        }

        public Esettings FindServerElevator(uint id)
        {
            foreach (Esettings bn in ElevatorUp)
            {
                if (bn.netid == id)
                {
                    return bn;
                }
            }
            return null;
        }

        object OnElevatorMove(Elevator e)
        {
            //Check if its a replaced elevator
            Esettings ThisElevator = FindServerElevator(e.net.ID);
            if (ThisElevator != null)
            {
                //Change floors if it is
                if (!Up.Contains(e.net.ID))
                {
                    GoToFloor(ThisElevator.Floors, e, ThisElevator);
                }
                else
                {
                    GoToFloor(1, e, ThisElevator);
                }
            }
            return null;
        }

        void GoToFloor(int floor, Elevator e, Esettings eset)
        {
            if(e.HasFlag(BaseEntity.Flags.Busy))
            {
                return;
            }
            Vector3 vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(0, floor, 0));
            float timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.y + vector.y));
            LeanTween.moveLocalY(e.liftEntity.gameObject, vector.y, timeToTravel);
            e.SendNetworkUpdateImmediate();
            e.SetFlag(global::BaseEntity.Flags.Busy, true, false, true);
            e.Invoke(new Action(e.ClearBusy), timeToTravel);
            if (floor == 1)
            {
                if (Up.Contains(e.net.ID))
                {
                    Up.Remove(e.net.ID);
                }
            }
            else
            {
                if (!Up.Contains(e.net.ID))
                {
                    Up.Add(e.net.ID);
                }
            }
            if (eset.AutoReturn)
            {
                timer.Once(eset.ReturnTime + timeToTravel, () =>
                {
                    if (Up.Contains(e.net.ID))
                    {
                        GoToFloor(1, e, eset);
                    }
                });
            }
        }

        [ChatCommand("elevator")]
        private void SetSettings(BasePlayer player, string command, string[] args)
        {
            try
            {
                if (player == null || !player.IPlayer.HasPermission(USE_PERM)) return;
                if (args.Length < 2 || args.Length < 1)
                {
                    player.ChatMessage("Help:\r\nYou must provide settings such as\r\n/elevator arg value\r\nSettings:\r\nfloors int (use neg to go down)\r\nreturn bool (true/flase auto return)\r\ndelay int (sec before return)\r\nreset yes (resets all server elevators)");
                }
                var elevatorList = FindElevator(player.transform.position, 3);
                if (elevatorList.Count == 0 && args[0] != "reset")
                {
                    player.ChatMessage("No Elevators nearby!");
                    return;
                }
                if (args[0] == "reset" && args[1] == "yes")
                {
                    player.ChatMessage("Resetting Data");
                    resetdata();
                    return;
                }
                foreach (var entity in elevatorList)
                {
                    Elevator x = entity as Elevator;
                    if (x == null)
                    {
                        continue;
                    }
                    if (Up.Contains(x.net.ID))
                    {
                        player.ChatMessage("Elevator must be at its base height to change setting");
                        return;
                    }
                    int index = 0;
                    foreach (Esettings es in ElevatorUp)
                    {
                        if (es.netid == x.net.ID)
                        {
                            break;
                        }
                        index++;
                    }
                    int newfloors = 2;
                    int newdelay = 60;
                    bool newreturn = true;
                    switch (args[0])
                    {
                        case "floors":
                            try
                            {
                                newfloors = int.Parse(args[1]);
                                if (newfloors == 1 || newfloors == 0)
                                {
                                    newfloors = 2;
                                }
                                ElevatorUp[index].Floors = newfloors;
                                player.ChatMessage("Changed floors to " + newfloors.ToString());
                                return;
                            }
                            catch
                            {
                                player.ChatMessage("Must set a number as floors");
                                return;
                            }
                        case "return":
                            try
                            {
                                newreturn = Boolean.Parse(args[1]);
                                ElevatorUp[index].AutoReturn = newreturn;
                                player.ChatMessage("Changed Auto return to " + newreturn.ToString());
                                return;
                            }
                            catch
                            {
                                player.ChatMessage("Must set true / false");
                                return;
                            }
                        case "delay":
                            try
                            {
                                newdelay = int.Parse(args[1]);
                                if (newdelay < 5)
                                {
                                    player.ChatMessage("Use a value greater than 5");
                                    return;
                                }
                                ElevatorUp[index].ReturnTime = newdelay;
                                player.ChatMessage("Changed Auto return delay to " + newdelay.ToString() + " sec");
                                return;
                            }
                            catch
                            {
                                player.ChatMessage("Must set a number as seconds of delay");
                                return;
                            }
                    }
                }
            }
            catch { }
        }
        public class Esettings
        {
            public Esettings()
            {
            }
            public Esettings(Vector3 p, Vector3 r, uint n)
            {
                this.pos = p;
                this.rot = r;
                this.netid = n;
                this.Floors = 2;
                this.ReturnTime = 60;
                this.AutoReturn = true;
            }
            public Esettings(Vector3 p, Vector3 r, uint n, int f, int rt, bool at)
            {
                this.pos = p;
                this.rot = r;
                this.netid = n;
                this.Floors = f;
                this.ReturnTime = rt;
                this.AutoReturn = at;
            }
            public uint netid;
            public Vector3 pos;
            public Vector3 rot;
            public int Floors;
            public int ReturnTime;
            public bool AutoReturn;
        }
    }
}