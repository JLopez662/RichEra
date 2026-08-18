// Feed page — telling a dead YouTube embed from a live one.
//
// Some Shorts refuse to play in an embed even though the channel allows embedding, and nothing
// YouTube serves ahead of time says which ones. The player is the only oracle, so the card asks
// it: enablejsapi=1 lets the frame post its state back, and anything that isn't playback inside
// the deadline is reported to the post, which drops back to its poster with an "open in YouTube"
// escape hatch.
//
// This lives in JS rather than .NET because it's a postMessage handshake on a timer — marshalling
// each poll into Blazor would buy nothing. Only one short plays at a time (the feed enforces it),
// so one watcher is enough.
(function () {
    'use strict';

    var watching = null;
    var HANDSHAKE_MS = 300, DEADLINE_MS = 10000, MAX_TRIES = 30;

    function clearWatch() {
        if (!watching) return;
        clearInterval(watching.poll);
        clearTimeout(watching.deadline);
        watching = null;
    }

    function settle(playing) {
        if (!watching || watching.settled) return;
        watching.settled = true;

        var ref = watching.ref;
        clearWatch();
        if (playing) return;

        // The post owns what a dead frame looks like; this only reports it.
        try { ref.invokeMethodAsync('OnEmbedFailed'); } catch (e) { /* post already gone */ }
    }

    window.addEventListener('message', function (e) {
        if (!watching || watching.settled) return;
        if (e.origin.indexOf('youtube') === -1) return;

        var msg;
        try { msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; } catch (_) { return; }
        if (!msg || !msg.event) return;

        if (msg.event === 'onError') { settle(false); return; }

        // 1 = playing, 3 = buffering. Either one means this video is fine here.
        var state = msg.info && typeof msg.info === 'object' ? msg.info.playerState : msg.info;
        if (state === 1 || state === 3) settle(true);
    });

    window.richEraShorts = {
        // Called by ShortPost once its player frame is in the DOM.
        watch: function (frame, ref) {
            clearWatch();
            if (!frame) return;

            watching = { ref: ref, settled: false, poll: null, deadline: null };

            // The player only starts talking once it has been asked to, and it isn't listening yet
            // when the frame first appears — so keep asking until it answers or we give up.
            var tries = 0;
            watching.poll = setInterval(function () {
                if (!watching || watching.settled || tries++ > MAX_TRIES) return;
                try {
                    frame.contentWindow.postMessage(
                        '{"event":"listening","id":1,"channel":"widget"}', '*');
                } catch (_) { /* frame not ready */ }
            }, HANDSHAKE_MS);

            // Silence is also an answer: a video that won't play here never reaches playing.
            watching.deadline = setTimeout(function () { settle(false); }, DEADLINE_MS);
        },

        // The post stopped playing (another short took over, or the feed was reshuffled).
        stop: clearWatch
    };
})();
