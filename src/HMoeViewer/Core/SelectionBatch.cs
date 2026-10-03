using System;

namespace HMoeViewer.Core;

public readonly record struct SelectionBatch(string DatabasePath, DateTimeOffset WriteTime);
