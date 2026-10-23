// Lets the game tell the web page whether it wants the mouse captured right now, and where the PLAY or PLAY AGAIN button is. Browsers, Safari especially,
// only allow capturing the mouse from inside the page's own click handler, so the page does it on the next click.
// See WebPage.cs and the ImposterInTown web template.
mergeInto(LibraryManager.library, {
  ImposterWantsPointerLock: function (on) {
    window.imposterWantsPointerLock = on !== 0;
  },
  // where the PLAY or PLAY AGAIN button is, as fractions of the screen from the bottom left, or nowhere (width 0)
  ImposterPlayButton: function (x, y, width, height) {
    window.imposterPlayButton = width > 0 ? { x: x, y: y, width: width, height: height } : null;
  },
  // whether the game's sound is running yet: browsers hold it back until the first click (Unity's own WEBAudio, which queues sounds until then)
  ImposterAudioRunning: function () {
    return typeof WEBAudio !== "undefined" && WEBAudio.contextIsRunning ? 1 : 0;
  },
});
