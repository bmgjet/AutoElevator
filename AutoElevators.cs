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
        private static SaveData _data; //Save Data for between server restarts
        private const string USE_PERM = "AutoElevators.use"; //Permission required for /elevator chat commands
        public List<Esettings> ElevatorUp = new List<Esettings>(); //Local settings file same as saved data
        public float ScanRadius = 0.8f; //Radius to use when buttons scan for elevator.

        class SaveData
        {
            public List<Esettings> ElevatorUp = new List<Esettings>();
        }

        private void WriteSaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private void Init()
        {
            permission.RegisterPermission(USE_PERM, this);
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(Name))
            {
                Interface.Oxide.DataFileSystem.GetDatafile(Name).Save();
            }
            _data = Interface.Oxide.DataFileSystem.ReadObject<SaveData>(Name);
            if (_data == null)
            {
                WriteSaveData();
            }
        }

        void OnServerInitialized()
        {
            //Delay in loading data for when server restarts some times elevators wouldnt be fully spawned in.
            timer.Once(5f, () =>
            {
                ReloadData();
            });
        }
        void OnServerSave()
        {
            DoSave();
        }

        void DoSave()
        {
            _data.ElevatorUp = ElevatorUp;
            WriteSaveData();
        }

        void Unload()
        {
            ClearElevators();
            try
            {
                DoSave();
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
                            //create with set settings
                            update.Add(el.net.ID);
                            _data.ElevatorUp.Add(new Esettings(el.transform.position, el.transform.rotation.eulerAngles, el.net.ID, es.Floors, es.ReturnTime, es.AutoReturn, es.Speed, es.Direction, es.Custompos));
                            flag = true;
                            break;
                        }
                    }
                    if (!flag)
                    {
                        //create new with default settings
                        _data.ElevatorUp.Add(new Esettings(el.transform.position, el.transform.rotation.eulerAngles, el.net.ID));
                    }
                    el.SendNetworkUpdateImmediate();
                }
            }
            //clean up old netids from last session
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
            //sync save data to local data.
            ElevatorUp = _data.ElevatorUp;
        }

        void resetdata()
        {
            //Clears data for a fresh start/map
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
                        //remove elevator if not train entrance since will be replaced by plugin.
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
            //Elevator buttons trigger this
            if (e.OwnerID == 0)
            {
                ElevatorLogic(e);
            }
            return null;
        }

        object OnButtonPress(PressButton button, BasePlayer player)
        {
            if (button.OwnerID == 0) //Faster exit so doesnt scan player placed buttons
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector3 dir;
                    //Checks up and down path incase button is above or below elevator.
                    if (i == 0)
                    {
                        dir = Vector3.down;
                    }
                    else
                    {
                        dir = Vector3.up;
                    }
                    List<Elevator> e = FindElevator(button.transform.position, ScanRadius, dir);
                    foreach (Elevator elevator in e)
                    {
                        Esettings es = FindServerElevator(elevator.net.ID);
                        if (es != null)
                        {
                            //Found elevator trigger it.
                            ElevatorLogic(elevator, es);
                            return null;
                        }
                    }
                }
            }
            return null;
        }

        void ElevatorLogic(Elevator e, Esettings ThisElevator = null)
        {
            //Check if its a plugin replaced elevator
            if (ThisElevator == null)
            {
                ThisElevator = FindServerElevator(e.net.ID);
            }
            if (ThisElevator != null)
            {
                //Change floors if it is
                //Check if elevator already triggered
                if (!ThisElevator.up)
                {
                    GoToFloor(ThisElevator.Floors, e, ThisElevator);
                }
                else
                {
                    //return to base height
                    GoToFloor(1, e, ThisElevator);
                }
            }
        }

        void GoToFloor(int floor, Elevator e, Esettings eset)
        {
            if (e.HasFlag(BaseEntity.Flags.Busy))
            {
                //Already moving so ignore command
                return;
            }
            //Set elevator speed
            e.LiftSpeedPerMetre = eset.Speed;
            //Set up base variables
            Vector3 vector = new Vector3(0, 1, 0);
            float timeToTravel = 0f;

            switch (eset.Direction)
            {
                //Up Down
                case 0:
                    vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(0, floor, 0));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.y + vector.y));
                    LeanTween.moveLocalY(e.liftEntity.gameObject, vector.y, timeToTravel);
                    break;
                //Forward Back
                case 1:
                    vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(floor, 0, 0));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.x + vector.x));
                    LeanTween.moveLocalX(e.liftEntity.gameObject, vector.x, timeToTravel);
                    break;
                //Side To Side
                case 2:
                    vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(0, 0, floor));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.z + vector.z));
                    LeanTween.moveLocalZ(e.liftEntity.gameObject, vector.z, timeToTravel);
                    break;
                case 3:
                    //Custom position
                    if (!eset.up)
                    {
                        vector = e.transform.InverseTransformPoint(e.transform.position + eset.Custompos);
                    }
                    timeToTravel = e.TimeToTravelDistance(Vector3.Distance(e.transform.position, vector));
                    LeanTween.moveLocal(e.liftEntity.gameObject, vector, timeToTravel);
                    break;
                case 4:
                    //Rotate X
                    vector = e.transform.InverseTransformPoint(e.transform.rotation.eulerAngles + new Vector3(floor, 0, 0));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.rotation.eulerAngles.x + vector.x));
                    if (!eset.up)
                    {
                        LeanTween.rotateX(e.liftEntity.gameObject, vector.x, timeToTravel);
                    }
                    else
                    {
                        LeanTween.rotateX(e.liftEntity.gameObject, 0, timeToTravel);
                    }
                    break;
                case 5:
                    //Rotate Y
                    vector = e.transform.InverseTransformPoint(e.transform.rotation.eulerAngles + new Vector3(0, floor, 0));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.rotation.eulerAngles.y + vector.y));
                    if (!eset.up)
                    {
                        LeanTween.rotateY(e.liftEntity.gameObject, vector.y, timeToTravel);
                    }
                    else
                    {
                        LeanTween.rotateY(e.liftEntity.gameObject, 0, timeToTravel);
                    }
                    break;
                case 6:
                    //Rotate Z
                    vector = e.transform.InverseTransformPoint(e.transform.rotation.eulerAngles + new Vector3(0, 0, floor));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.rotation.eulerAngles.z + vector.z));
                    if (!eset.up)
                    {
                        LeanTween.rotateZ(e.liftEntity.gameObject, vector.z, timeToTravel);
                    }
                    else
                    {
                        LeanTween.rotateZ(e.liftEntity.gameObject, 0, timeToTravel);
                    }
                    break;
                case 7:
                    //Throw player off
                    vector = e.transform.InverseTransformPoint(e.transform.position + new Vector3(floor, floor, floor));
                    timeToTravel = e.TimeToTravelDistance(Mathf.Abs(e.liftEntity.transform.localPosition.y + vector.y));
                    LeanTween.moveLocalY(e.liftEntity.gameObject, vector.z, timeToTravel);
                    if (!eset.up)
                    {
                        LeanTween.rotateZ(e.liftEntity.gameObject, 180, timeToTravel);
                    }
                    else
                    {
                        LeanTween.rotateZ(e.liftEntity.gameObject, 0, timeToTravel);
                    }
                    break;
            }
            e.SendNetworkUpdateImmediate();
            //Set busy flag
            e.SetFlag(global::BaseEntity.Flags.Busy, true, false, true);
            //set timer to disable busy flag
            e.Invoke(new Action(e.ClearBusy), timeToTravel);
            eset.up = !eset.up;
            //Set up auto return based on delay.
            if (eset.AutoReturn)
            {
                timer.Once(eset.ReturnTime + timeToTravel, () =>
                {
                    if (eset.up)
                    {
                        GoToFloor(1, e, eset);
                    }
                });
            }
        }

        //Chat setup commands
        [ChatCommand("elevator")]
        private void SetSettings(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IPlayer.HasPermission(USE_PERM)) return;
            if (args == null || args.Length == 0 || args.Length == 1 || args.Length >= 3)
            {
                player.ChatMessage("Help:\r\nYou must provide settings such as\r\n/elevator arg value\r\nSettings:\r\nfloors int (use neg to go down)\r\nreturn bool (true/flase auto return)\r\ndelay int (sec before return)\r\nspeed float (speed elevator moves)\r\ndirection int (0 = y, 1 = x, 2 = z)\r\nposition f|f|f (Vector to move to)\r\npos get (prints out players vector)\r\nsave yes (saves elevator settings straight away)\r\nreset yes (resets all server elevators)");
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
            if (args[0] == "save" && args[1] == "yes")
            {
                DoSave();
                player.ChatMessage("Settings Saved");
                return;
            }

            int index = -1;
            Elevator entity = null;
            List<Elevator> e = FindElevator(player.transform.position, 0.8f, Vector3.down);
            foreach (Elevator elevator in e)
            {
                Esettings es = FindServerElevator(elevator.net.ID);
                if (es != null)
                {
                    index = FindElevatorIndex(elevator.net.ID);
                    entity = elevator;
                    if (es.up)
                    {
                        player.ChatMessage("Elevator must be at its base height to change setting");
                        return;
                    }
                    break;
                }
            }
            if (entity == null)
            {
                player.ChatMessage("No Elevator Found, Stand on it and look down");
                return;
            }
                if (index >= ElevatorUp.Count || index == -1)
                {
                    player.ChatMessage("Cant find index");
                    return;
                }
                switch (args[0])
                {
                    case "floors":
                        try
                        {
                            int newfloors = int.Parse(args[1]);
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
                           bool newreturn = Boolean.Parse(args[1]);
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
                            int newdelay = int.Parse(args[1]);
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
                            float newspeed = (float)double.Parse(args[1]);
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
                            int newdirection = int.Parse(args[1]);
                            if (newdirection >= 0 && newdirection <= 7)
                            {
                                ElevatorUp[index].Direction = newdirection;
                                player.ChatMessage("Changed elevator direction");
                                return;

                            }
                            player.ChatMessage("Use a value of 0-7");
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
                            ElevatorUp[index].Custompos = new Vector3(float.Parse(customposition[0]), float.Parse(customposition[1]), float.Parse(customposition[2]));
                            player.ChatMessage("Custom positions set");
                            return;
                        }
                        catch
                        {
                            player.ChatMessage("Please provide a vector floats seperated by | such as x|y|z");
                            return;
                        }
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
                this.Speed = 2f;
                this.Direction = 0;
                this.Custompos = new Vector3(0, 0, 0);
                this.up = false;
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
                this.up = false;
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
            public bool up;
        }
    }
}