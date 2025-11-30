// Simple Express server to serve static files and provide /maps/list endpoint
const express = require('express');
const fs = require('fs');
const path = require('path');

const app = express();
const PORT = 3000;

// Serve static files from the root and public directory
app.use(express.static(__dirname));
app.use('/public', express.static(path.join(__dirname, 'public')));

// Endpoint to list all .json files in public/maps
app.get('/maps/list', (req, res) => {
    const mapsDir = path.join(__dirname, 'public', 'maps');
    fs.readdir(mapsDir, (err, files) => {
        if (err) {
            return res.status(500).json({ error: 'Failed to read maps directory' });
        }
        const jsonFiles = files.filter(f => f.endsWith('.json'));
        res.json(jsonFiles);
    });
});

app.listen(PORT, () => {
    console.log(`Server running at http://localhost:${PORT}`);
});
