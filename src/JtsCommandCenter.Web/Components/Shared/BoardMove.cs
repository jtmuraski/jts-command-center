namespace JtsCommandCenter.Web.Components.Shared;

/// <summary>A card dropped on a <see cref="KanbanBoard{TItem, TColumn}"/>: which card, into which column, at what position.</summary>
public readonly record struct BoardMove<TColumn>(int Id, TColumn Column, int Index);
