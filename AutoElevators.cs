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
        public float ScanRadius = 0.8f;

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
            timer.Once(5f, () =>
            {
                ReloadData();
            });
        }
        void OnServerSave()
        {
            _data.ElevatorUp = ElevatorUp;
            WriteSaveData();
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
                        Puts("Fault creating elevator!");
                    }
                    foreach (Esettings es in _data.ElevatorUp)
                    {
                        //Check for settings
                        if (es.pos == el.transform.position)
                        {
                            //create with settings
                            update.Add(el.net.ID);
                            _data.ElevatorUp.Add(new Esettings(el.transform.position, el.transform.rotation.ToEuler(), el.net.ID, es.Floors, es.ReturnTime, es.AutoReturn, es.Speed,es.Direction,es.Custompos));
                            flag = true;
                            break;
                        }
                    }
                    if (!flag)
                    {
                        //create new
                        _data.ElevatorUp.Add(new Esettings(el.transform.position, el.transform.rotation.ToEuler(), el.net.ID));
                    }
                    el.SendNetworkUpdateImmediate();
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
            ElevatorUp.Clear();
            _data.ElevatorUp.Clear();
            WriteSaveData();
            ReloadData();
        }

        public void ClearElevators()
        {
            //Delete any exsisting elevators
            int test = 0;
            foreach (BaseNetworkable bn in BaseNetworkable.serverEntities)
            {
                if (bn.prefabID == 3845190333)
                {
                    //Scan area to make sure not train entance
                    var hits = Physics.SphereCastAll(bn.transform.position, 6f, Vector3.down);
                    bool train = false;
                    foreach (var hit in hits)
                    {
                        if (hit.GetEntity()?.prefabID == 1802909967)
                        {
                            test++;
                            train = true;
                            break;
                        }
                    }
                    if (!train)
                    {
                        bn.Kill();
                    }
                }
            }
            Puts("Train Elevators Found " + test.ToString());
        }

        private List<Elevator> FindElevator(Vector3 pos, float radius, Vector3 dir)
        {
            var hits = Physics.SphereCastAll(pos, radius, dir);
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

        public int FindElevatorIndex(uint id)
        {
            int index = 0;
            foreach (Esettings bn in ElevatorUp)
            {
                if (bn.netid == id)
                {
                    return index;
                }
                index++;
            }
            return -1;
        }

        object OnElevatorMove(Elevator e)
        {
            ElevatorLogic(e);
            return null;
        }

        void ElevatorLogic(Elevator e)
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
        }

        void path(int floor, Elevator e, Esettings eset)
        {
            Vector3 vector = new Vector3(0, 0, 0);
            Puts(floor.ToString());
            if (floor == 1)
            {
                vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(0, 1, 0));
            }
            else
            {
                vector = e.transform.InverseTransformPoint(e.transform.position + eset.Custompos);
            }
            e.LiftSpeedPerMetre = eset.Speed;
            float timeToTravel = e.TimeToTravelDistance(Vector3.Distance(e.transform.position, vector));
            LeanTween.moveLocal(e.liftEntity.gameObject, vector, timeToTravel);
            e.SendNetworkUpdateImmediate();
            e.SetFlag(global::BaseEntity.Flags.Busy, true, false, true);
            e.Invoke(new Action(e.ClearBusy), timeToTravel);
            ElevatorEndLogic(floor, e, eset, timeToTravel);
        }

        void GoToFloor(int floor, Elevator e, Esettings eset, int axis = 0)
        {
            if (e.HasFlag(BaseEntity.Flags.Busy))
            {
                return;
            }
            axis = eset.Direction;
            if (axis == 3)
            {
                path(floor, e, eset);
                return;
            }
            e.LiftSpeedPerMetre = eset.Speed;
            Vector3 vector = new Vector3(0, 0, 0);
            float timeToTravel = 0f;
            switch (axis)
            {

                case 0:
                    vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(0, floor, 0));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.y + vector.y));
                    LeanTween.moveLocalY(e.liftEntity.gameObject, vector.y, timeToTravel);
                    break;
                case 1:
                    vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(floor, 0, 0));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.x + vector.x));
                    LeanTween.moveLocalX(e.liftEntity.gameObject, vector.x, timeToTravel);
                    break;
                case 2:
                    vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(0, 0, floor));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.z + vector.z));
                    LeanTween.moveLocalZ(e.liftEntity.gameObject, vector.z, timeToTravel);
                    break;

            }
            e.SendNetworkUpdateImmediate();
            e.SetFlag(global::BaseEntity.Flags.Busy, true, false, true);
            e.Invoke(new Action(e.ClearBusy), timeToTravel);
            ElevatorEndLogic(floor, e, eset, timeToTravel);
        }

        private void ElevatorEndLogic(int floor, Elevator e, Esettings eset, float returndelay)
        {
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
                timer.Once(eset.ReturnTime + returndelay, () =>
                {
                    if (Up.Contains(e.net.ID))
                    {
                        GoToFloor(1, e, eset);
                    }
                });
            }
        }

        object OnButtonPress(PressButton button, BasePlayer player)
        {
            //Scan down path.
            List<Elevator> e = FindElevator(button.transform.position, ScanRadius, Vector3.down);
            foreach (Elevator elevator in e)
            {
                Esettings es = FindServerElevator(elevator.net.ID);
                if (es != null)
                {
                    ElevatorLogic(elevator);
                    return null;
                }
            }
            //Scan up path
            List<Elevator> e2 = FindElevator(button.transform.position, ScanRadius, Vector3.up);
            foreach (Elevator elevator in e2)
            {
                Esettings es = FindServerElevator(elevator.net.ID);
                if (es != null)
                {
                    ElevatorLogic(elevator);
                    return null;
                }
            }
            return null;
        }

        [ChatCommand("elevator")]
        private void SetSettings(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IPlayer.HasPermission(USE_PERM)) return;
            if (args == null || args.Length == 0 || args.Length == 1 || args.Length >= 3)
            {
                player.ChatMessage("Help:\r\nYou must provide settings such as\r\n/elevator arg value\r\nSettings:\r\nfloors int (use neg to go down)\r\nreturn bool (true/flase auto return)\r\ndelay int (sec before return)\r\nspeed float (speed elevator moves)\r\ndirection int (0 = y, 1 = x, 2 = z)\r\nreset yes (resets all server elevators)");
                return;
            }
            if (ElevatorUp.Count == 0)
            {
                player.ChatMessage("_data file not found");
                return;
            }
            if (args[0] == "reset" && args[1] == "yes")
            {
                player.ChatMessage("Resetting Data");
                resetdata();
                return;
            }
            if (args[0] == "pos" && args[1] == "get")
            {
                player.ChatMessage("You are currently @ " + player.transform.position.ToString());
                return;
            }

            int index = 0;
            Elevator entity = null;
            List<Elevator> e = FindElevator(player.transform.position, 0.8f, Vector3.down);
            foreach (Elevator elevator in e)
            {
                Esettings es = FindServerElevator(elevator.net.ID);
                if (es != null)
                {
                    index = FindElevatorIndex(elevator.net.ID);
                    entity = elevator;
                }
            }
            if (entity != null)
            {
                if (Up.Contains(entity.net.ID))
                {
                    player.ChatMessage("Elevator must be at its base height to change setting");
                    return;
                }
                if (index >= ElevatorUp.Count || index == -1)
                {
                    player.ChatMessage("Cant find index");
                    return;
                }
                int newfloors = 2;
                int newdelay = 60;
                bool newreturn = true;
                float newspeed = 1.5f;
                int newdirection = 0;
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
                    case "speed":
                        try
                        {
                            newspeed = (float)double.Parse(args[1]);
                            if (newspeed <= 0)
                            {
                                player.ChatMessage("Use a value greater than 1");
                                return;
                            }
                            ElevatorUp[index].Speed = newspeed;
                            player.ChatMessage("Changed elevator speed to " + newspeed.ToString() + " per m");
                            return;
                        }
                        catch
                        {
                            player.ChatMessage("Must set a float as speed per m");
                            return;
                        }
                    case "direction":
                        try
                        {
                            newdirection = int.Parse(args[1]);
                            if (newdirection == 0 || newdirection == 1 || newdirection == 2 || newdirection == 3)
                            {
                                ElevatorUp[index].Direction = newdirection;
                                player.ChatMessage("Changed elevator direction");
                                return;

                            }
                            player.ChatMessage("Use a value of 0, 1 or 2");
                            return;
                        }
                        catch
                        {
                            player.ChatMessage("Must set a int as direction 0 = y, 1 = x, 2 = z");
                            return;
                        }
                    case "position":
                        try
                        {
                            string[] customposition = args[1].Split('|');
                            Vector3 newcustompos = new Vector3(float.Parse(customposition[0]), float.Parse(customposition[1]), float.Parse(customposition[2]));
                            ElevatorUp[index].Custompos = newcustompos;
                            player.ChatMessage("Custom position set " + newcustompos.ToString());
                            return;
                        }
                        catch
                        {
                            player.ChatMessage("Please provide a vector floats seperated by | such as x|y|z");
                            return;
                        }
                }
            }
            else
            {
                player.ChatMessage("No Elevator Found, Stand on it and look down");
                return;
            }
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
                this.Speed = 1.5f;
                this.Direction = 0;
                this.Custompos = new Vector3(0, 0, 0);
            }
            public Esettings(Vector3 p, Vector3 r, uint n, int f, int rt, bool at, float s, int d, Vector3 c)
            {
                this.pos = p;
                this.rot = r;
                this.netid = n;
                this.Floors = f;
                this.ReturnTime = rt;
                this.AutoReturn = at;
                this.Speed = s;
                this.Direction = d;
                this.Custompos = c;
            }
            public uint netid;
            public Vector3 pos;
            public Vector3 rot;
            public int Floors;
            public int ReturnTime;
            public bool AutoReturn;
            public float Speed;
            public int Direction;
            public Vector3 Custompos;
        }
    }
}