using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AIBridge.Models;

namespace AIBridge.Services;

public class CodingAgentRegistry : ICodingAgentRegistry
{
    private readonly ConcurrentDictionary<string, ICodingAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedAgentId = "antigravity";

    public IEnumerable<CodingAgentDescriptor> GetAgents()
    {
        return _agents.Values.Select(a => a.Descriptor);
    }

    public ICodingAgent? GetAgent(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        _agents.TryGetValue(id, out var agent);
        return agent;
    }

    public ICodingAgent GetSelectedAgent()
    {
        if (_agents.TryGetValue(_selectedAgentId, out var agent))
        {
            return agent;
        }

        return _agents.Values.FirstOrDefault()
            ?? throw new InvalidOperationException("No coding agents registered in CodingAgentRegistry.");
    }

    public void RegisterAgent(ICodingAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agents[agent.Id] = agent;
    }

    public void SetSelectedAgent(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Agent Id cannot be empty.", nameof(id));
        if (!_agents.ContainsKey(id)) throw new InvalidOperationException($"Coding agent '{id}' is not registered.");
        _selectedAgentId = id;
    }
}
