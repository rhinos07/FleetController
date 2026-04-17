class FleetDashboard extends HTMLElement {
    connectedCallback() {
        this.innerHTML = `
            <section>
                <h1>Fleet Manager Dashboard</h1>
                <div id="status">Connecting to SignalR...</div>
                <h2>Events</h2>
                <pre id="events"></pre>
            </section>`;

        this.statusElement = this.querySelector('#status');
        this.eventsElement = this.querySelector('#events');
        this.connect();
    }

    async connect() {
        if (typeof signalR === 'undefined') {
            this.statusElement.textContent = 'SignalR client library unavailable';
            return;
        }

        const connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/dashboard')
            .withAutomaticReconnect()
            .build();

        const appendEvent = (label, payload) => {
            const text = `[${new Date().toISOString()}] ${label}: ${JSON.stringify(payload)}`;
            this.eventsElement.textContent = `${text}\n${this.eventsElement.textContent}`;
        };

        connection.on('orderUpdated', payload => appendEvent('orderUpdated', payload));
        connection.on('vehicleUpdated', payload => appendEvent('vehicleUpdated', payload));
        connection.on('zoneBlockChanged', payload => appendEvent('zoneBlockChanged', payload));

        await connection.start();
        this.statusElement.textContent = 'Connected';
    }
}

customElements.define('fleet-dashboard', FleetDashboard);
