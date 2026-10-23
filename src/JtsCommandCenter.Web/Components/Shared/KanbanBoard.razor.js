// Drag-and-drop for KanbanBoard, using the global Sortable (wwwroot/lib/sortablejs).
// Blazor owns the DOM, so a drop puts the card back where it was and tells .NET about the move;
// the component then re-renders the card in its new place.

export function init(board, dotNet) {
    const group = `board-${Math.random().toString(36).slice(2)}`;

    const sortables = [...board.querySelectorAll("[data-board-list]")].map(list =>
        Sortable.create(list, {
            group,
            draggable: "[data-board-card]",
            // Buttons inside a card (its menu) shouldn't start a drag.
            filter: "[data-no-drag]",
            preventOnFilter: false,
            // Pointer-event dragging behaves the same with a mouse, a pen, and touch.
            forceFallback: true,
            fallbackTolerance: 4,
            // On touch, a short press picks a card up, so a quick swipe still scrolls the board.
            delay: 200,
            delayOnTouchOnly: true,
            animation: 150,
            ghostClass: "is-ghost",
            chosenClass: "is-chosen",
            dragClass: "is-dragging",
            onEnd: event => {
                const { item, from, to, oldIndex, newIndex, newDraggableIndex } = event;
                if (from === to && oldIndex === newIndex) {
                    return;
                }

                item.remove();
                from.insertBefore(item, from.childNodes[oldIndex] ?? null);

                dotNet.invokeMethodAsync("OnDrop", Number(item.dataset.id), to.dataset.column, newDraggableIndex);
            },
        }));

    return {
        dispose: () => sortables.forEach(sortable => sortable.destroy()),
    };
}
