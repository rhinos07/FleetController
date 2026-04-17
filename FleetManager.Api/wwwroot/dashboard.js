class FleetDashboard extends HTMLElement {
    connectedCallback() {
        const section = document.createElement('section');
        const heading = document.createElement('h1');
        heading.textContent = 'Fleet Manager Dashboard';
        section.appendChild(heading);

        this.statusElement = document.createElement('div');
        this.statusElement.id = 'status';
        this.statusElement.textContent = 'Connecting to SignalR...';
        section.appendChild(this.statusElement);

        const eventsHeading = document.createElement('h2');
        eventsHeading.textContent = 'Events';
        section.appendChild(eventsHeading);

        this.eventsElement = document.createElement('pre');
        this.eventsElement.id = 'events';
        section.appendChild(this.eventsElement);

        this.replaceChildren(section);
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
