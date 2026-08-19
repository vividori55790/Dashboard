/**
 * ⚡ TelemetryClient.js (v2.0) - Universal Real-Time Telemetry & AI Stream Client SDK
 * 
 * Provides a lightweight, zero-dependency JavaScript SDK for integrating custom HTML,
 * React, Vue, Svelte, and Mobile Web Dashboards with the TelemetryDashboard backend.
 * 
 * @example
 * // 1. Include in HTML:
 * // <script src="http://localhost:8080/telemetry-client.js"></script>
 * 
 * // 2. Connect and listen to data in 3 lines:
 * TelemetryClient.connect('ws://localhost:8080/ws');
 * TelemetryClient.onData((data) => {
 *     console.log('Received:', data.nodeId, data.temp);
 * });
 */
(function (global) {
    class TelemetryClientSDK {
        constructor() {
            this.ws = null;
            this.wsUrl = 'ws://localhost:8080/ws';
            this.httpBaseUrl = 'http://localhost:8080';
            this.dataListeners = [];
            this.anomalyListeners = [];
            this.channelListeners = {};
            this.statusListeners = [];
            this.isConnected = false;
            this.reconnectTimer = null;
            this.reconnectAttempts = 0;
            this.maxReconnectAttempts = 50;
            this.totalPacketsReceived = 0;

            // Transport actually carrying data: 'websocket', 'sse', or null when down.
            // Exposed because a dashboard that fell back should be able to say so — a client
            // silently running on the slower path looks identical to one that never degraded.
            this.transport = null;

            this.sse = null;
            // Consecutive WebSocket attempts that never reached onopen. Corporate proxies and some
            // mobile carriers pass HTTP but drop the Upgrade handshake, so a socket that repeatedly
            // fails to open is the signal to try Server-Sent Events instead of retrying forever.
            this.failedWsHandshakes = 0;
            this.handshakeFailuresBeforeFallback = 2;
        }

        /**
         * Connects to the TelemetryDashboard WebSocket streaming server.
         * @param {string} [url='ws://localhost:8080/ws'] 
         */
        connect(url = 'ws://localhost:8080/ws') {
            this.wsUrl = url;
            if (url.startsWith('ws://') || url.startsWith('wss://')) {
                this.httpBaseUrl = url.replace('ws://', 'http://').replace('wss://', 'https://').replace(/\/ws\/?$/, '');
            }

            if (this.ws && (this.ws.readyState === WebSocket.OPEN || this.ws.readyState === WebSocket.CONNECTING)) {
                return;
            }

            try {
                this.ws = new WebSocket(this.wsUrl);

                this.ws.onopen = () => {
                    this.isConnected = true;
                    this.transport = 'websocket';
                    this.reconnectAttempts = 0;
                    this.failedWsHandshakes = 0;
                    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
                    console.log('%c[TelemetryClient] 🟢 Connected to ' + this.wsUrl, 'color:#00ff9d; font-weight:bold;');
                    this._notifyStatus('CONNECTED');
                };

                this.ws.onmessage = (event) => this._dispatch(event.data);

                this.ws.onclose = () => {
                    // A close before any open means the handshake itself was refused, which is what
                    // a proxy blocking Upgrade looks like — distinct from a socket that worked and
                    // then dropped, which is worth retrying as a socket.
                    if (this.transport !== 'websocket') this.failedWsHandshakes++;

                    this.isConnected = false;
                    this.transport = null;
                    this._notifyStatus('DISCONNECTED');

                    if (this.failedWsHandshakes >= this.handshakeFailuresBeforeFallback) {
                        this._startSseFallback();
                        return;
                    }
                    this._scheduleReconnect();
                };

                this.ws.onerror = (err) => {
                    console.warn('[TelemetryClient] WebSocket connection error:', err);
                    this.isConnected = false;
                    this._notifyStatus('ERROR');
                };
            } catch (ex) {
                console.error('[TelemetryClient] Connect exception:', ex);
                this.failedWsHandshakes++;
                this._scheduleReconnect();
            }
        }

        /**
         * Routes one raw frame to the listeners, whatever transport delivered it.
         * Shared so WebSocket and SSE cannot drift into behaving differently.
         * @private
         */
        _dispatch(raw) {
            let packet;
            try {
                packet = JSON.parse(raw);
            } catch (err) {
                console.warn('[TelemetryClient] Payload JSON Parse Error:', err);
                return;
            }

            this.totalPacketsReceived++;

            for (let i = 0; i < this.dataListeners.length; i++) {
                try { this.dataListeners[i](packet); } catch (e) { console.error(e); }
            }

            const channelId = packet.nodeId || packet.device || 'DEFAULT';
            if (this.channelListeners[channelId]) {
                this.channelListeners[channelId].forEach(cb => {
                    try { cb(packet); } catch (e) { }
                });
            }

            // An anomaly verdict of 0 may mean "scored calm" or "never scored"; only act on a
            // score the server actually sent.
            if ((packet.anomalyScore !== undefined && packet.anomalyScore >= 2.5) || packet.isAnomaly) {
                for (let i = 0; i < this.anomalyListeners.length; i++) {
                    try { this.anomalyListeners[i](packet); } catch (e) { console.error(e); }
                }
            }
        }

        /**
         * Switches to Server-Sent Events over plain HTTP.
         *
         * This is what keeps a phone or a machine behind a corporate proxy usable. The desktop
         * shell is Windows-only by design, so a browser is the only way in from anywhere else —
         * and a client that can only speak WebSocket has no way in at all when Upgrade is blocked.
         * The server has served `/stream` all along; nothing here reached for it.
         * @private
         */
        _startSseFallback() {
            if (typeof EventSource === 'undefined') {
                console.warn('[TelemetryClient] No EventSource in this browser; staying on WebSocket retries.');
                this._scheduleReconnect();
                return;
            }

            if (this.sse) return;

            const streamUrl = this.httpBaseUrl + '/stream';
            console.warn('[TelemetryClient] WebSocket handshake refused; falling back to SSE at ' + streamUrl);

            try {
                this.sse = new EventSource(streamUrl);

                this.sse.onopen = () => {
                    this.isConnected = true;
                    this.transport = 'sse';
                    this._notifyStatus('CONNECTED_SSE');
                };

                this.sse.onmessage = (event) => this._dispatch(event.data);

                this.sse.onerror = () => {
                    // EventSource retries on its own; report the gap rather than tearing it down,
                    // because closing here would forfeit the only transport still available.
                    this.isConnected = false;
                    this.transport = null;
                    this._notifyStatus('ERROR');
                };
            } catch (ex) {
                console.error('[TelemetryClient] SSE fallback failed:', ex);
                this.sse = null;
                this._scheduleReconnect();
            }
        }

        /**
         * Schedules the next attempt with exponential backoff, then keeps trying slowly.
         *
         * The previous version stopped permanently after 50 attempts — roughly four minutes — and
         * only said so in the console. A dashboard left open on a wall display or a phone would
         * then sit dead through an overnight server restart, showing its last frame as though it
         * were current. Retrying forever at a slow cadence costs one request a minute and is the
         * difference between a screen that recovers and one that lies.
         * @private
         */
        _scheduleReconnect() {
            if (this.reconnectTimer) clearTimeout(this.reconnectTimer);

            this.reconnectAttempts++;

            const fastPhase = this.reconnectAttempts < this.maxReconnectAttempts;
            const delay = fastPhase
                ? Math.min(5000, 1000 * Math.pow(1.3, this.reconnectAttempts))
                : 60000;

            if (!fastPhase && this.reconnectAttempts === this.maxReconnectAttempts) {
                console.warn('[TelemetryClient] Still down; backing off to one attempt per minute.');
                this._notifyStatus('RETRYING_SLOWLY');
            }

            this.reconnectTimer = setTimeout(() => {
                console.log(`[TelemetryClient] 🔄 Reconnecting (Attempt ${this.reconnectAttempts})...`);
                this.connect(this.wsUrl);
            }, delay);
        }

        /** Closes whichever transport is open and stops retrying. */
        disconnect() {
            if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;

            if (this.sse) {
                this.sse.close();
                this.sse = null;
            }
            if (this.ws) {
                // Detach first: onclose would otherwise schedule a reconnect the caller just asked
                // to stop.
                this.ws.onclose = null;
                this.ws.close();
                this.ws = null;
            }

            this.isConnected = false;
            this.transport = null;
            this._notifyStatus('DISCONNECTED');
        }

        _notifyStatus(status) {
            this.statusListeners.forEach(cb => {
                try { cb(status, this.isConnected); } catch (e) { }
            });
        }

        /**
         * Registers a callback invoked whenever any telemetry packet arrives.
         * @param {function(object): void} callback 
         */
        onData(callback) {
            if (typeof callback === 'function') {
                this.dataListeners.push(callback);
            }
            return this;
        }

        /**
         * Registers a callback invoked only when an anomaly is detected (Z-Score >= 2.5).
         * @param {function(object): void} callback 
         */
        onAnomaly(callback) {
            if (typeof callback === 'function') {
                this.anomalyListeners.push(callback);
            }
            return this;
        }

        /**
         * Registers a callback for a specific nodeId/channel.
         * @param {string} channelId 
         * @param {function(object): void} callback 
         */
        onChannel(channelId, callback) {
            if (typeof callback === 'function') {
                if (!this.channelListeners[channelId]) {
                    this.channelListeners[channelId] = [];
                }
                this.channelListeners[channelId].push(callback);
            }
            return this;
        }

        /**
         * Registers a callback invoked when connection status changes ('CONNECTED', 'DISCONNECTED', 'ERROR').
         * @param {function(string, boolean): void} callback 
         */
        onStatusChange(callback) {
            if (typeof callback === 'function') {
                this.statusListeners.push(callback);
                // Immediately notify current state
                callback(this.isConnected ? 'CONNECTED' : 'DISCONNECTED', this.isConnected);
            }
            return this;
        }

        /**
         * Fetches latest AI Incident Report in Markdown from the backend HTTP API.
         * @returns {Promise<{status: string, anomalyCount: number, markdown: string}>}
         */
        async getIncidentReport() {
            try {
                const res = await fetch(`${this.httpBaseUrl}/api/dvr/report`);
                return await res.json();
            } catch (err) {
                console.error('[TelemetryClient] Fetch report error:', err);
                return { status: 'Error', markdown: 'Failed to fetch incident report.' };
            }
        }

        /**
         * Fetches server status and packet metrics.
         * @returns {Promise<{server: string, status: string, port: number, connectedClients: number, totalPackets: number}>}
         */
        async getServerStatus() {
            try {
                const res = await fetch(`${this.httpBaseUrl}/api/status`);
                return await res.json();
            } catch (err) {
                return { server: 'Unknown', status: 'Offline' };
            }
        }
    }

    // Export singleton instance as global.TelemetryClient
    global.TelemetryClient = new TelemetryClientSDK();
})(typeof window !== 'undefined' ? window : this);
