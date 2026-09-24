using System.Collections.Generic;
using AIBridge.Models;

namespace AIBridge.Services;

public interface ICodingAgentRegistry
{
    IEnumerable<CodingAgentDescriptor> GetAgents();
    ICodingAgent? GetAgent(string id);
    ICodingAgent GetSelectedAgent();
    void RegisterAgent(ICodingAgent agent);
    void SetSelectedAgent(string id);
}
