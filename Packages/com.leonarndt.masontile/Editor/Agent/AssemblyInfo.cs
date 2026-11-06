using System.Runtime.CompilerServices;

// Tool methods stay internal (like the AI Assistant's own tools); the integration tests call them directly.
[assembly: InternalsVisibleTo("MasonTile.Agent.Tests")]
