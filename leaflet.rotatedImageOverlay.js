(function() {
    // save these original methods before they are overwritten
    var proto_animateZoom = L.ImageOverlay.prototype._animateZoom;
    var proto_reset = L.ImageOverlay.prototype._reset;

    L.ImageOverlay.addInitHook(function () {
        this.options.rotationOrigin = this.options.rotationOrigin || 'center center';
    });

    L.ImageOverlay.include({
        _animateZoom: function(e) {
            proto_animateZoom.call(this, e);
            this._applyRotation();
        },

        _reset: function () {
            proto_reset.call(this);
            this._applyRotation();
        },

        _applyRotation: function () {
            if(this.options.rotationAngle) {
                this._image.style[L.DomUtil.TRANSFORM+'Origin'] = this.options.rotationOrigin;

                if(L.Browser.ie3d) {
                    // for IE 9, use the 2D rotation
                    this._image.style[L.DomUtil.TRANSFORM] += ' rotate(' + this.options.rotationAngle + 'deg)';
                } else {
                    // for modern browsers, prefer the 3D accelerated version
                    this._image.style[L.DomUtil.TRANSFORM] += ' rotateZ(' + this.options.rotationAngle + 'deg)';
                }
            }
        },

        setRotationAngle: function(angle) {
            this.options.rotationAngle = angle;
            if (this._map) {
                this._reset();
            }
            return this;
        },

        setRotationOrigin: function(origin) {
            this.options.rotationOrigin = origin;
            if (this._map) {
                this._reset();
            }
            return this;
        }
    });
})();
