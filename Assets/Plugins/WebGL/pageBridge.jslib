// Lets the game tell the web page whether it wants the mouse captured right now, and where the PLAY button is. Browsers, Safari especially,
// only allow capturing the mouse from inside the page's own click handler, so the page does it on the next click.
// See WebPage.cs and the ImposterInTown web template.
mergeInto(LibraryManager.library, {
  ImposterWantsPointerLock: function (on) {
    window.imposterWantsPointerLock = on !== 0;
  },
  // where the start screen's PLAY button is, as fractions of the screen from the bottom left, or nowhere (width 0)
  ImposterPlayButton: function (x, y, width, height) {
    window.imposterPlayButton = width > 0 ? { x: x, y: y, width: width, height: height } : null;
  },
});
