using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using GTA.Math;
using System.IO;

public class HighwayAICarSpawner : Script
{
    private class AIVehicle
    {
        public Vehicle Vehicle;
        public Ped Driver;
        public bool HasReactedToLights;

        public AIVehicle(Vehicle vehicle, Ped driver)
        {
            Vehicle = vehicle;
            Driver = driver;
            HasReactedToLights = false;
        }
    }

    private readonly List<AIVehicle> spawnedAI = new List<AIVehicle>();
    private DateTime lastSpawnTime = DateTime.MinValue;
    private static readonly Random random = new Random();

    private bool playerLightsActive;
    private bool previousLightsState;

    // ---------------- INI CONFIG VALUES ----------------
    private List<string> vehicleModels = new List<string>();
    private float minDistance = 300f;
    private float maxDistance = 900f;
    private int spawnIntervalSeconds = 10;

    // NEW: notification toggle
    private bool showSpawnNotifications = false;

    // NEW: Emergency reaction distance
    private float emergencyReactionDistance = 150f;

    // ---------------- Spawn Points ----------------
    private readonly (Vector3 pos, float heading)[] spawnPoints =
    {
        (new Vector3(699.51f, -186.70f, 46.50f), 338.18f),
        (new Vector3(1300.75f, 573.51f, 79.92f), 322.24f),
        (new Vector3(1732.05f, 1569.41f, 84.16f), 347.38f),
        (new Vector3(1098.81f, -1780.63f, 28.81f), 206.80f),
        (new Vector3(865.21f, -670.09f, 42.74f), 58.99f),
        (new Vector3(-59.44f, -484.27f, 31.70f), 94.77f),
        (new Vector3(-310.73f, -538.73f, 24.86f), 279.10f),
        (new Vector3(-407.70f, -1506.49f, 37.01f), 353.14f),
        (new Vector3(-1613.20f, -756.05f, 11.15f), 248.35f),

        (new Vector3(1815.76f, 2193.65f, 53.60f), 170.97f),
        (new Vector3(2088.07f, 1382.91f, 75.11f), 213.62f),
        (new Vector3(2375.49f, -270.02f, 84.48f), 150.01f),
        (new Vector3(1529.60f, -1025.42f, 57.31f), 303.48f),
        (new Vector3(2477.27f, -136.99f, 89.21f), 335.19f),
        (new Vector3(2124.81f, 1381.37f, 75.00f), 39.03f),

        (new Vector3(2471.31f, 2929.05f, 40.33f), 310.99f),
        (new Vector3(1995.04f, 2598.59f, 54.06f), 141.76f),
        (new Vector3(-2627.62f, 2922.35f, 16.40f), 175.27f),
        (new Vector3(2940.40f, 4010.55f, 51.10f), 10.96f),
        (new Vector3(2892.37f, 4027.71f, 50.89f), 199.45f),
        (new Vector3(2591.48f, 520.36f, 44.49f), 181.99f),

        (new Vector3(-2980.97f, 100.57f, 13.82f), 237.33f),
        (new Vector3(-2275.62f, 4245.20f, 43.32f), 149.57f),
        (new Vector3(-3140.68f, 908.63f, 14.18f), 5.60f),

        (new Vector3(1929.91f, 6284.47f, 42.34f), 206.60f),
        (new Vector3(2404.25f, 5800.16f, 45.65f), 33.84f),
        (new Vector3(484.21f, 6576.69f, 26.70f), 88.32f),
        (new Vector3(-569.96f, 5666.72f, 38.06f), 334.48f),
    };

    public HighwayAICarSpawner()
    {
        LoadConfig();
        Interval = 100;
        Tick += OnTick;
    }

    private void LoadConfig()
    {
        string iniPath = "scripts\\morespeeders.ini";

        if (!File.Exists(iniPath))
            return;

        ScriptSettings ini = ScriptSettings.Load(iniPath);

        string models = ini.GetValue("Vehicles", "Models", "");
        vehicleModels.Clear();

        foreach (string s in models.Split(','))
        {
            string trimmed = s.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                vehicleModels.Add(trimmed);
        }

        minDistance = ini.GetValue("SpawnDistance", "MinDistance", 300f);
        maxDistance = ini.GetValue("SpawnDistance", "MaxDistance", 900f);
        spawnIntervalSeconds = ini.GetValue("Timing", "SpawnIntervalSeconds", 10);

        // NEW: Load emergency reaction distance
        emergencyReactionDistance = ini.GetValue("Emergency", "ReactionDistance", 150f);

        // NEW: notification toggle
        showSpawnNotifications = ini.GetValue("Notifications", "ShowSpawnNotifications", false);
    }

    private void OnTick(object sender, EventArgs e)
    {
        previousLightsState = playerLightsActive;
        CheckPlayerEmergencyLights();

        // Check continuously when lights are on (not just when first turned on)
        if (playerLightsActive)
        {
            SwitchAIToNormalDriving();
        }

        CleanupOldVehicles();

        if ((DateTime.Now - lastSpawnTime).TotalSeconds >= spawnIntervalSeconds)
        {
            TrySpawnTrafficAIVehicle();
            lastSpawnTime = DateTime.Now;
        }
    }

    private void TrySpawnTrafficAIVehicle()
    {
        if (vehicleModels.Count == 0) return;

        Ped player = Game.Player.Character;
        Vector3 playerPos = player.Position;

        List<int> indices = new List<int>();
        for (int i = 0; i < spawnPoints.Length; i++) indices.Add(i);

        while (indices.Count > 0)
        {
            int index = indices[random.Next(indices.Count)];
            indices.Remove(index);

            var spawn = spawnPoints[index];
            float dist = playerPos.DistanceTo(spawn.pos);

            if (dist >= minDistance && dist <= maxDistance)
            {
                SpawnVehicleAt(spawn.pos, spawn.heading);
                return;
            }
        }
    }

    private void SpawnVehicleAt(Vector3 pos, float heading)
    {
        pos = World.GetNextPositionOnStreet(pos);
        if (World.GetClosestVehicle(pos, 5f) != null) return;

        string modelName = vehicleModels[random.Next(vehicleModels.Count)];
        Model model = new Model(modelName);
        model.Request(2000);
        if (!model.IsLoaded) return;

        Vehicle veh = World.CreateVehicle(model, pos, heading);
        model.MarkAsNoLongerNeeded();
        if (veh == null) return;

        veh.IsPersistent = true;

        Model pedModel = new Model(PedHash.Business01AMM);
        pedModel.Request(500);
        Ped driver = World.CreatePed(pedModel, pos);
        pedModel.MarkAsNoLongerNeeded();

        if (driver == null)
        {
            veh.Delete();
            return;
        }

        driver.SetIntoVehicle(veh, VehicleSeat.Driver);
        driver.IsPersistent = true;
        driver.BlockPermanentEvents = true;

        StartDrivingTask(driver, veh, 120f, 786603u);

        spawnedAI.Add(new AIVehicle(veh, driver));

        if (showSpawnNotifications)
            ShowNotification($"AI Vehicle Spawned\nModel: {modelName}");
    }

    private void SwitchAIToNormalDriving()
    {
        Ped player = Game.Player.Character;

        foreach (var ai in spawnedAI)
        {
            if (ai.HasReactedToLights) continue;
            if (!ai.Vehicle.Exists() || !ai.Driver.Exists()) continue;

            // Check distance to player
            float distanceToPlayer = ai.Vehicle.Position.DistanceTo(player.Position);
            if (distanceToPlayer > emergencyReactionDistance) continue;

            // CRITICAL FIX: Clear ALL tasks completely
            ai.Driver.Task.ClearAll();

            // Let GTA's native traffic AI take over completely
            ai.Driver.DrivingStyle = DrivingStyle.Normal;

            // Remove mission entity flag so they behave like normal traffic
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ai.Driver.Handle, false, true);
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ai.Vehicle.Handle, false, true);

            // Set them to just cruise like normal traffic (not drive-to-coord)
            ai.Driver.Task.CruiseWithVehicle(ai.Vehicle, 25f, 786603);

            // Make them obey traffic laws
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, ai.Driver.Handle, 0.0f);
            Function.Call(Hash.SET_DRIVER_ABILITY, ai.Driver.Handle, 0.5f);

            // Ensure they can be controlled by traffic system
            ai.Vehicle.IsDriveable = true;
            ai.Driver.CanBeDraggedOutOfVehicle = true;
            ai.Driver.CanWrithe = false;

            // Mark them as normal traffic
            ai.HasReactedToLights = true;

            if (showSpawnNotifications)
                ShowNotification("AI switched to normal traffic behavior");
        }
    }

    private void StartDrivingTask(Ped driver, Vehicle vehicle, float mphSpeed, uint style)
    {
        Vector3 dest = World.GetNextPositionOnStreet(vehicle.Position + vehicle.ForwardVector * 2000f);

        Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver.Handle, style);
        Function.Call(
            Hash.TASK_VEHICLE_DRIVE_TO_COORD,
            driver,
            vehicle,
            dest.X, dest.Y, dest.Z,
            mphSpeed * 0.44704f,
            1,
            vehicle.Model.Hash,
            style,
            10f,
            10f
        );
    }

    private void CheckPlayerEmergencyLights()
    {
        Ped p = Game.Player.Character;
        if (!p.IsInVehicle()) { playerLightsActive = false; return; }

        Vehicle v = p.CurrentVehicle;
        playerLightsActive =
            v != null &&
            v.Exists() &&
            Function.Call<bool>(Hash.IS_VEHICLE_SIREN_ON, v.Handle);
    }

    private void CleanupOldVehicles()
    {
        Ped player = Game.Player.Character;

        for (int i = spawnedAI.Count - 1; i >= 0; i--)
        {
            var ai = spawnedAI[i];

            if (!ai.Vehicle.Exists())
            {
                spawnedAI.RemoveAt(i);
                continue;
            }

            if (!ai.Driver.Exists()) continue;
            if (!ai.Driver.IsInVehicle(ai.Vehicle)) continue;

            if (ai.Vehicle.Position.DistanceTo(player.Position) > maxDistance)
            {
                ai.Vehicle.Delete();
                ai.Driver.Delete();
                spawnedAI.RemoveAt(i);
            }
        }
    }

    private void ShowNotification(string message)
    {
        Function.Call(Hash._SET_NOTIFICATION_TEXT_ENTRY, "STRING");
        Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, message);
        Function.Call(Hash._DRAW_NOTIFICATION, false, true);
    }
}