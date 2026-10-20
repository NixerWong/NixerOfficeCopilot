using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerEndpointInfo
    {
        public int ProcessId { get; set; }

        public string ProcessStartedAtUtc { get; set; }

        public string ServerName { get; set; }

        public string PipeName { get; set; }

        public string RegisteredAtUtc { get; set; }
    }

    public sealed class AgentBrokerEndpointRegistration : IDisposable
    {
        private readonly string _filePath;
        private int _disposed;

        internal AgentBrokerEndpointRegistration(string filePath)
        {
            _filePath = filePath;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }

    public static class AgentBrokerEndpointRegistry
    {
        public static AgentBrokerEndpointRegistration Register(string pipeName, string serverName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("A broker pipe name is required.", nameof(pipeName));
            }

            if (string.IsNullOrWhiteSpace(serverName))
            {
                throw new ArgumentException("A broker server name is required.", nameof(serverName));
            }

            var directoryPath = GetDirectoryPath();
            Directory.CreateDirectory(directoryPath);

            using (var process = Process.GetCurrentProcess())
            {
                var endpoint = new AgentBrokerEndpointInfo
                {
                    ProcessId = process.Id,
                    ProcessStartedAtUtc = process.StartTime.ToUniversalTime().ToString("o"),
                    ServerName = serverName,
                    PipeName = pipeName,
                    RegisteredAtUtc = DateTime.UtcNow.ToString("o")
                };

                var fileName = endpoint.ProcessId + "-" + Guid.NewGuid().ToString("N") + ".json";
                var filePath = Path.Combine(directoryPath, fileName);
                var temporaryPath = filePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(endpoint), new UTF8Encoding(false));
                File.Move(temporaryPath, filePath);
                return new AgentBrokerEndpointRegistration(filePath);
            }
        }

        public static IReadOnlyList<AgentBrokerEndpointInfo> GetForProcess(int processId)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }

            var directoryPath = GetDirectoryPath();
            var endpoints = new List<AgentBrokerEndpointInfo>();
            if (!Directory.Exists(directoryPath))
            {
                return endpoints.AsReadOnly();
            }

            DateTime processStartedAtUtc;
            using (var process = Process.GetProcessById(processId))
            {
                processStartedAtUtc = process.StartTime.ToUniversalTime();
            }

            foreach (var filePath in Directory.GetFiles(directoryPath, processId + "-*.json"))
            {
                AgentBrokerEndpointInfo endpoint;
                try
                {
                    endpoint = JsonConvert.DeserializeObject<AgentBrokerEndpointInfo>(
                        File.ReadAllText(filePath, Encoding.UTF8));
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException(
                        "The broker endpoint registration is invalid: " + filePath,
                        ex);
                }

                if (endpoint == null
                    || endpoint.ProcessId != processId
                    || string.IsNullOrWhiteSpace(endpoint.ProcessStartedAtUtc)
                    || string.IsNullOrWhiteSpace(endpoint.ServerName)
                    || string.IsNullOrWhiteSpace(endpoint.PipeName))
                {
                    throw new InvalidDataException(
                        "The broker endpoint registration is incomplete: " + filePath);
                }

                DateTime registeredProcessStart;
                if (!DateTime.TryParse(
                    endpoint.ProcessStartedAtUtc,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out registeredProcessStart))
                {
                    throw new InvalidDataException(
                        "The broker endpoint registration has an invalid process start time: " + filePath);
                }

                if (Math.Abs((registeredProcessStart.ToUniversalTime() - processStartedAtUtc).TotalSeconds) > 2)
                {
                    continue;
                }

                endpoints.Add(endpoint);
            }

            return endpoints.AsReadOnly();
        }

        public static string GetDirectoryPath()
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException("The local application data directory is unavailable.");
            }

            return Path.Combine(localApplicationData, "NixerOfficeCopilot", "BrokerEndpoints");
        }
    }
}
