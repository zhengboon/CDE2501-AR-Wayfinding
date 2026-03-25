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
    generatedAt.textContent = '2026-03-25';
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
