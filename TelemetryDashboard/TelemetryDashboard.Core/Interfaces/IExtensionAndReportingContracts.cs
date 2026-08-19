using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Interfaces;

/// <summary>Source of installable extensions.</summary>
public interface IMarketplaceService
{
    /// <summary>Fetches the catalogue. Throws on transport failure so callers can show an offline state.</summary>
    Task<List<ExtensionDescriptor>> FetchAvailableExtensionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Publishes generated reports to a Notion database.</summary>
public interface INotionClient
{
    /// <summary>Creates a report page and returns its Notion page id.</summary>
    Task<string> CreateReportPageAsync(string databaseId, string title, List<TelemetryPacket> packets);
}

/// <summary>Publishes alerts to a Slack incoming webhook.</summary>
public interface ISlackClient
{
    /// <summary>Posts an alert. Returns false rather than throwing when delivery fails.</summary>
    Task<bool> SendAlertAsync(string webhookUrl, string message);
}
