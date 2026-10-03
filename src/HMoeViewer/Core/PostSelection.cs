using System;
using HMoeData.Models;

namespace HMoeViewer.Core;

public readonly record struct PostSelection(int Id, PostSelectionState State, DateTimeOffset WriteTime);
