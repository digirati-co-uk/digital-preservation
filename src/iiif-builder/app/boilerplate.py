def get_boilerplate_manifest():
    return {
        "type": "Manifest",
        "provider": [
            {
                "id": "https://library.leeds.ac.uk/info/1600/about",
                "type": "Agent",
                "label": {"en": ["University of Leeds"]},
                "homepage": [
                    {
                        "id": "https://library.leeds.ac.uk/",
                        "type": "Text",
                        "label": {"en": ["Leeds University Library Homepage"]},
                        "format": "text/html"
                    }
                ],
                "logo": [
                    {
                        "id": "https://resources.library.leeds.ac.uk/logo/black.png",
                        "type": "Image",
                        "format": "image/png",
                        "height": 61,
                        "width": 300
                    }
                ]
            }
        ]
    }
