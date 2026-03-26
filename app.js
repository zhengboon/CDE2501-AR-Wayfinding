// Smooth scrolling
const anchors = document.querySelectorAll('a[href^="#"]');
anchors.forEach(anchor => {
    anchor.addEventListener('click', function onClick(event) {
        event.preventDefault();
        const target = document.querySelector(this.getAttribute('href'));
        if (target) {
            window.scrollTo({ top: target.offsetTop - 72, behavior: 'smooth' });
        }
    });
});

// Mobile navigation
const hamburger = document.getElementById('hamburger');
const navLinks = document.getElementById('navLinks');
if (hamburger && navLinks) {
    hamburger.addEventListener('click', () => {
        hamburger.classList.toggle('open');
        navLinks.classList.toggle('open');
    });

    navLinks.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            hamburger.classList.remove('open');
            navLinks.classList.remove('open');
        });
    });
}

// Scroll reveal
const observer = new IntersectionObserver((entries, obs) => {
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            entry.target.classList.add('is-visible');
            obs.unobserve(entry.target);
        }
    });
}, { threshold: 0.12 });

document.querySelectorAll('.show-on-scroll').forEach(node => observer.observe(node));

// Navbar section highlighting
const sections = document.querySelectorAll('section[id]');
const links = document.querySelectorAll('.nav-links a');

window.addEventListener('scroll', () => {
    const scrollY = window.pageYOffset;
    let current = '';
    sections.forEach(section => {
        if (scrollY >= section.offsetTop - 120) {
            current = section.id;
        }
    });

    links.forEach(link => {
        link.classList.toggle('active', link.getAttribute('href') === `#${current}`);
    });
}, { passive: true });

// Copy command buttons
document.querySelectorAll('.btn-copy').forEach(button => {
    button.addEventListener('click', async () => {
        const targetId = button.getAttribute('data-copy-target');
        const source = targetId ? document.getElementById(targetId) : null;
        if (!source) {
            return;
        }

        const text = source.textContent || '';
        try {
            await navigator.clipboard.writeText(text);
            const previous = button.textContent;
            button.textContent = 'Copied';
            setTimeout(() => {
                button.textContent = previous;
            }, 1000);
        } catch (_) {
            // Clipboard API may be unavailable on file:// contexts.
            button.textContent = 'Copy failed';
            setTimeout(() => {
                button.textContent = 'Copy';
            }, 1200);
        }
    });
});

const generatedAt = document.getElementById('generatedAt');
if (generatedAt) {
    generatedAt.textContent = '2026-03-26';
}

// Hero waveform animation
(function runWaveAnimation() {
    const waveA = document.getElementById('wave-a');
    const waveB = document.getElementById('wave-b');
    if (!waveA || !waveB) {
        return;
    }

    const width = 960;
    const midpoint = 60;
    let time = 0;

    const buildPoints = fn => {
        const points = [];
        for (let x = 0; x <= width; x += 4) {
            points.push(`${x},${fn(x)}`);
        }
        return points.join(' ');
    };

    const waveOne = x => midpoint - 28 * Math.sin((x / width) * Math.PI * 6 + time) * Math.cos(time * 0.35 + x * 0.003);
    const waveTwo = x => midpoint - 22 * (1 - Math.exp(-((x / width + time * 0.05) % 1) * 5)) * Math.cos(time * 0.48);

    const tick = () => {
        time += 0.018;
        waveA.setAttribute('points', buildPoints(waveOne));
        waveB.setAttribute('points', buildPoints(waveTwo));
        requestAnimationFrame(tick);
    };

    requestAnimationFrame(tick);
})();

// Google Street View Tiles demo (Queenstown)
(function initStreetViewTilesDemo() {
    const apiKeyInput = document.getElementById('gsvApiKey');
    const loadButton = document.getElementById('gsvLoadBtn');
    const latInput = document.getElementById('gsvLat');
    const lngInput = document.getElementById('gsvLng');
    const headingInput = document.getElementById('gsvHeading');
    const pitchInput = document.getElementById('gsvPitch');
    const headingValue = document.getElementById('gsvHeadingValue');
    const pitchValue = document.getElementById('gsvPitchValue');
    const statusEl = document.getElementById('gsvStatus');
    const metaEl = document.getElementById('gsvMeta');
    const canvas = document.getElementById('gsvCanvas');

    if (!apiKeyInput || !loadButton || !latInput || !lngInput || !headingInput || !pitchInput || !headingValue || !pitchValue || !statusEl || !metaEl || !canvas) {
        return;
    }

    const ctx = canvas.getContext('2d');
    if (!ctx) {
        return;
    }

    const storageKey = 'gsv_api_key';
    const defaultLanguage = 'en-US';
    const defaultRegion = 'SG';
    const panoSearchRadius = 80;
    const renderFovDegrees = 100;
    const maxPitchDegrees = 45;

    let panoramaCanvas = null;
    let panoramaWidth = 0;
    let panoramaHeight = 0;
    let loading = false;

    const toNumber = (rawValue, fallback) => {
        const value = Number.parseFloat(rawValue);
        return Number.isFinite(value) ? value : fallback;
    };

    const setStatus = (message, isError = false) => {
        statusEl.textContent = message;
        statusEl.style.color = isError ? '#fda4af' : '#7dd3fc';
    };

    const updateControlLabels = () => {
        headingValue.textContent = `${Math.round(toNumber(headingInput.value, 0))}°`;
        pitchValue.textContent = `${Math.round(toNumber(pitchInput.value, 0))}°`;
    };

    const drawPlaceholder = () => {
        const gradient = ctx.createLinearGradient(0, 0, canvas.width, canvas.height);
        gradient.addColorStop(0, '#0f172a');
        gradient.addColorStop(1, '#1e293b');
        ctx.fillStyle = gradient;
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.fillStyle = '#cbd5e1';
        ctx.font = '24px Outfit, sans-serif';
        ctx.fillText('Street View preview will appear here', 40, canvas.height / 2 - 8);
        ctx.font = '16px Inter, sans-serif';
        ctx.fillStyle = '#94a3b8';
        ctx.fillText('Use your API key and click Load Queenstown View', 40, canvas.height / 2 + 24);
    };

    const fetchJson = async (url, options = {}) => {
        const response = await fetch(url, options);
        const text = await response.text();
        let payload = {};
        try {
            payload = text ? JSON.parse(text) : {};
        } catch (_) {
            payload = {};
        }

        if (!response.ok) {
            const errorMessage = payload?.error?.message || `Request failed (${response.status})`;
            throw new Error(errorMessage);
        }

        return payload;
    };

    const loadImage = url => new Promise((resolve, reject) => {
        const image = new Image();
        image.crossOrigin = 'anonymous';
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error('Failed to download one of the Street View tiles.'));
        image.src = url;
    });

    const runWithConcurrencyLimit = async (tasks, limit) => {
        const workers = Array.from({ length: Math.min(limit, tasks.length) }, async () => {
            while (tasks.length > 0) {
                const task = tasks.shift();
                if (!task) {
                    continue;
                }
                await task();
            }
        });
        await Promise.all(workers);
    };

    const drawViewport = () => {
        if (!panoramaCanvas || panoramaWidth <= 0 || panoramaHeight <= 0) {
            drawPlaceholder();
            return;
        }

        const normalizedHeading = ((toNumber(headingInput.value, 0) % 360) + 360) % 360;
        const rawPitch = toNumber(pitchInput.value, 0);
        const clampedPitch = Math.max(-maxPitchDegrees, Math.min(maxPitchDegrees, rawPitch));
        const sourceWidth = Math.max(2, Math.floor((renderFovDegrees / 360) * panoramaWidth));
        const targetAspect = canvas.width / canvas.height;
        const sourceHeight = Math.max(2, Math.min(panoramaHeight, Math.floor(sourceWidth / targetAspect)));

        const centerX = (normalizedHeading / 360) * panoramaWidth;
        const verticalRange = (panoramaHeight - sourceHeight) / 2;
        const centerY = panoramaHeight / 2 - (clampedPitch / maxPitchDegrees) * verticalRange;

        let sourceX = Math.round(centerX - sourceWidth / 2);
        const sourceY = Math.round(Math.max(0, Math.min(panoramaHeight - sourceHeight, centerY - sourceHeight / 2)));

        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';

        const wrappedX = ((sourceX % panoramaWidth) + panoramaWidth) % panoramaWidth;
        if (wrappedX + sourceWidth <= panoramaWidth) {
            ctx.drawImage(
                panoramaCanvas,
                wrappedX,
                sourceY,
                sourceWidth,
                sourceHeight,
                0,
                0,
                canvas.width,
                canvas.height
            );
            return;
        }

        const firstWidth = panoramaWidth - wrappedX;
        const secondWidth = sourceWidth - firstWidth;
        const firstDestWidth = Math.round(canvas.width * (firstWidth / sourceWidth));

        ctx.drawImage(
            panoramaCanvas,
            wrappedX,
            sourceY,
            firstWidth,
            sourceHeight,
            0,
            0,
            firstDestWidth,
            canvas.height
        );
        ctx.drawImage(
            panoramaCanvas,
            0,
            sourceY,
            secondWidth,
            sourceHeight,
            firstDestWidth,
            0,
            canvas.width - firstDestWidth,
            canvas.height
        );
    };

    const loadStreetView = async () => {
        if (loading) {
            return;
        }

        const apiKey = apiKeyInput.value.trim();
        if (!apiKey) {
            setStatus('API key is required.', true);
            return;
        }

        const lat = toNumber(latInput.value, 1.2945);
        const lng = toNumber(lngInput.value, 103.7863);
        if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
            setStatus('Latitude and longitude must be valid numbers.', true);
            return;
        }

        loading = true;
        loadButton.disabled = true;
        loadButton.textContent = 'Loading...';
        metaEl.textContent = '';

        try {
            localStorage.setItem(storageKey, apiKey);

            setStatus('Creating Street View session...');
            const sessionPayload = await fetchJson(`https://tile.googleapis.com/v1/createSession?key=${encodeURIComponent(apiKey)}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    mapType: 'streetview',
                    language: defaultLanguage,
                    region: defaultRegion
                })
            });

            const session = sessionPayload.session;
            if (!session) {
                throw new Error('No session token returned.');
            }

            setStatus('Locating nearest panorama in Queenstown...');
            const panoPayload = await fetchJson(
                `https://tile.googleapis.com/v1/streetview/panoIds?session=${encodeURIComponent(session)}&key=${encodeURIComponent(apiKey)}`,
                {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        locations: [{ lat, lng }],
                        radius: panoSearchRadius
                    })
                }
            );

            const panoId = Array.isArray(panoPayload?.panoIds) ? panoPayload.panoIds[0] : '';
            if (!panoId) {
                throw new Error('No Street View panorama found near that location.');
            }

            const metadata = await fetchJson(
                `https://tile.googleapis.com/v1/streetview/metadata?session=${encodeURIComponent(session)}&key=${encodeURIComponent(apiKey)}&panoId=${encodeURIComponent(panoId)}`
            );

            const tileWidth = metadata.tileWidth || sessionPayload.tileWidth || 512;
            const tileHeight = metadata.tileHeight || sessionPayload.tileHeight || 512;
            const imageWidth = metadata.imageWidth;
            const imageHeight = metadata.imageHeight;

            if (!imageWidth || !imageHeight || !tileWidth || !tileHeight) {
                throw new Error('Missing panorama dimensions from metadata.');
            }

            const maxZoom = Math.ceil(Math.log2(Math.max(imageWidth / tileWidth, imageHeight / tileHeight)));
            const previewZoom = Math.max(0, maxZoom - 2);
            const downscale = 2 ** (maxZoom - previewZoom);
            panoramaWidth = Math.ceil(imageWidth / downscale);
            panoramaHeight = Math.ceil(imageHeight / downscale);
            const xTiles = Math.ceil(panoramaWidth / tileWidth);
            const yTiles = Math.ceil(panoramaHeight / tileHeight);
            const totalTiles = xTiles * yTiles;

            panoramaCanvas = document.createElement('canvas');
            panoramaCanvas.width = panoramaWidth;
            panoramaCanvas.height = panoramaHeight;
            const panoContext = panoramaCanvas.getContext('2d');
            if (!panoContext) {
                throw new Error('Cannot create panorama buffer.');
            }

            setStatus(`Downloading panorama tiles (0/${totalTiles})...`);
            let completedTiles = 0;
            const tileTasks = [];
            for (let x = 0; x < xTiles; x += 1) {
                for (let y = 0; y < yTiles; y += 1) {
                    tileTasks.push(async () => {
                        const tileUrl = `https://tile.googleapis.com/v1/streetview/tiles/${previewZoom}/${x}/${y}?session=${encodeURIComponent(session)}&key=${encodeURIComponent(apiKey)}&panoId=${encodeURIComponent(panoId)}`;
                        const tileImage = await loadImage(tileUrl);
                        panoContext.drawImage(tileImage, x * tileWidth, y * tileHeight);
                        completedTiles += 1;
                        if (completedTiles === totalTiles || completedTiles % 4 === 0) {
                            setStatus(`Downloading panorama tiles (${completedTiles}/${totalTiles})...`);
                        }
                    });
                }
            }

            await runWithConcurrencyLimit(tileTasks, 6);
            drawViewport();

            const captureDate = metadata.date || 'unknown';
            const approxHeading = Number.isFinite(metadata.heading) ? `${Math.round(metadata.heading)}°` : 'n/a';
            metaEl.textContent = `panoId: ${panoId} | captured: ${captureDate} | pano heading: ${approxHeading}`;
            setStatus(`Loaded Street View for ${lat.toFixed(6)}, ${lng.toFixed(6)}.`);
        } catch (error) {
            panoramaCanvas = null;
            panoramaWidth = 0;
            panoramaHeight = 0;
            drawPlaceholder();
            setStatus(`Load failed: ${error instanceof Error ? error.message : 'Unknown error'}`, true);
        } finally {
            loading = false;
            loadButton.disabled = false;
            loadButton.textContent = 'Load Queenstown View';
        }
    };

    headingInput.addEventListener('input', () => {
        updateControlLabels();
        drawViewport();
    });

    pitchInput.addEventListener('input', () => {
        updateControlLabels();
        drawViewport();
    });

    loadButton.addEventListener('click', loadStreetView);

    const savedApiKey = localStorage.getItem(storageKey);
    if (savedApiKey) {
        apiKeyInput.value = savedApiKey;
    }

    updateControlLabels();
    drawPlaceholder();
})();
